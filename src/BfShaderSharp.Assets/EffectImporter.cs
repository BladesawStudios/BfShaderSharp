using BfShaderSharp.Pack;
using BntxSharp;
using TexSharp;
using TxtgSharp;

namespace BfShaderSharp.Assets;

/// <summary>Imports the effect files (<c>Effect/&lt;name&gt;.Nin_NX_NVN.esetb.byml.zs</c>) of Tears of the Kingdom and the textures their emitters use.</summary>
/// <remarks>
/// An effect file is stored decoded, as game data <c>Effect/&lt;name&gt;</c>: a BYML holding the VFXB tree (key PtclBin) and the list of the textures the emitters name by id. Reading the tree is
/// the renderer's side. A texture is stored as <c>EffectTexture/&lt;name&gt;</c>, decoded to plain bytes a renderer can upload: the width and height as two 32-bit integers, then RGBA8 of the first
/// surface. The texture's name is its file's name in TexToGo.
/// </remarks>
public static class EffectImporter
{
    public static string FileEntry(string name) => $"Effect/{name}";

    public static string TextureEntry(string name) => $"EffectTexture/{name}";

    /// <summary>The decoded bytes of an effect file, or null when the romfs has none by that name.</summary>
    public static byte[]? ReadFile(RomfsFiles romfs, string name)
    {
        string path = Path.Combine(romfs.Root, "Effect", $"{name}.Nin_NX_NVN.esetb.byml.zs");
        return File.Exists(path) ? romfs.Read(path) : null;
    }

    /// <summary>Stores the effect files named, and the textures; returns the numbers stored.</summary>
    public static (int Files, int Textures) Import(AssetContext context, IReadOnlyCollection<string> files, IReadOnlyCollection<string> textures)
    {
        int storedFiles = 0, storedTextures = 0;
        foreach (string name in files)
        {
            if (ReadFile(context.Romfs, name) is not { } bytes) continue;
            context.Pack.AddEntry(PackEntryKind.GameData, FileEntry(name), context.Pack.Add(bytes));
            storedFiles++;
        }

        foreach (string name in textures)
        {
            if (Decode(context.Romfs, name) is not { } decoded) continue;
            using MemoryStream stream = new();
            using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(decoded.Width);
                writer.Write(decoded.Height);
                writer.Write(decoded.Rgba);
            }

            context.Pack.AddEntry(PackEntryKind.GameData, TextureEntry(name), context.Pack.Add(stream.ToArray()));
            storedTextures++;
        }

        return (storedFiles, storedTextures);
    }

    /// <summary>The first surface of a texture as RGBA8, or null when the texture has no file; a format that is not decoded throws.</summary>
    public static (int Width, int Height, byte[] Rgba)? Decode(RomfsFiles romfs, string name)
    {
        if (romfs.TextureFile(name) is not { } path) return null;
        if (!path.EndsWith(".txtg", StringComparison.OrdinalIgnoreCase))
        {
            BntxFile bntx = BntxFile.Load(romfs.Read(path));
            BntxTexture first = bntx.Textures.FirstOrDefault(t => t.Name == name) ?? bntx.Textures[0];
            return (first.Width, first.Height, first.ToRgba8());
        }

        TxtgFile texture = TxtgFile.FromBytes(romfs.Read(path));
        byte[] data = (texture.Surface(0, 0) ?? throw new InvalidDataException($"{path} has no first surface")).Data;
        byte[] rgba = texture.Format switch
        {
            TxtgFormat.Bc1Unorm or TxtgFormat.Bc1UnormSrgb => TextureDecoder.ToRgba8(TextureFormat.Bc1, data, texture.Width, texture.Height),
            TxtgFormat.Bc3UnormSrgb => TextureDecoder.ToRgba8(TextureFormat.Bc3, data, texture.Width, texture.Height),
            TxtgFormat.Bc4Unorm => TextureDecoder.ToRgba8(TextureFormat.Bc4, data, texture.Width, texture.Height),
            TxtgFormat.Bc5Unorm => TextureDecoder.ToRgba8(TextureFormat.Bc5, data, texture.Width, texture.Height),
            TxtgFormat.Bc7Unorm => TextureDecoder.ToRgba8(TextureFormat.Bc7, data, texture.Width, texture.Height),
            TxtgFormat.R8G8B8A8Unorm => data,
            _ => throw new NotSupportedException($"The effect texture '{name}' is {texture.Format}, which is not decoded."),
        };
        return (texture.Width, texture.Height, rgba);
    }
}
