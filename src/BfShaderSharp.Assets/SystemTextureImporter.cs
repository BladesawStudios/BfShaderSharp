using BntxSharp;
using BfShaderSharp.Pack;
using TexSharp;
using TxtgSharp;

namespace BfShaderSharp.Assets;

/// <summary>Reads the shared textures shaders sample but no material owns (the 3D noise volume, the sun disc and the eight moon phases), decodes them to one or two bytes per texel and stores each as game data <c>SystemTexture/&lt;name&gt;</c>.</summary>
/// <remarks>An entry is its width, height, depth and channel count as four little-endian 32-bit integers and then the texels, which is the file the renderer's system texture store reads. A texture the game lacks, or has in a format other than the one expected, is left out.</remarks>
public static class SystemTextureImporter
{
    public const string Group = "SystemTexture";
    private const string Noise = "3DWorleyPerlinNoise_Fi";

    public static string EntryName(string name) => $"{Group}/{name}";

    /// <summary>Stores the textures the romfs has; returns how many were stored.</summary>
    public static int Import(AssetContext context)
    {
        int stored = 0;
        stored += Try(() => ImportNoise(context));
        stored += Try(() => ImportMask(context, "Etc_Sun_A_Alb", "SunDisc"));
        for (int phase = 1; phase <= 8; phase++)
        {
            int p = phase;
            stored += Try(() => ImportRg(context, $"Etc_Moon_A_Alb.{p}", $"Moon{p}"));
        }

        return stored;
    }

    private static int Try(Func<bool> import)
    {
        try { return import() ? 1 : 0; }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or IndexOutOfRangeException) { return 0; }
    }

    private static bool ImportNoise(AssetContext context)
    {
        string? path = context.Romfs.TextureFile(Noise);
        if (path is null) return false;
        BntxTexture texture = BntxFile.Load(context.Romfs.Read(path)).Textures[0];
        int width = texture.Width, height = texture.Height, depth = texture.Depth;
        byte[] blocks = texture.GetDeswizzledData();
        int slice = blocks.Length / depth;
        byte[] decoded = new byte[width * height * depth];
        for (int z = 0; z < depth; z++) Red(TextureFormat.Bc4, blocks.AsSpan(z * slice, slice), width, height, decoded.AsSpan(z * width * height));
        Store(context, "Proc3DNoise", width, height, depth, 1, decoded);
        return true;
    }

    private static bool ImportMask(AssetContext context, string name, string entry)
    {
        if (Open(context, name, TxtgFormat.Bc4Unorm) is not { } file) return false;
        byte[] decoded = new byte[file.Width * file.Height];
        Red(TextureFormat.Bc4, file.Surface(0, 0)!.Data, file.Width, file.Height, decoded);
        Store(context, entry, file.Width, file.Height, 1, 1, decoded);
        return true;
    }

    private static bool ImportRg(AssetContext context, string name, string entry)
    {
        if (Open(context, name, TxtgFormat.Bc5Unorm) is not { } file) return false;
        byte[] rgba = TextureDecoder.ToRgba8(TextureFormat.Bc5, file.Surface(0, 0)!.Data, file.Width, file.Height);
        byte[] decoded = new byte[file.Width * file.Height * 2];
        for (int i = 0; i < file.Width * file.Height; i++)
        {
            decoded[i * 2] = rgba[i * 4];
            decoded[i * 2 + 1] = rgba[i * 4 + 1];
        }

        Store(context, entry, file.Width, file.Height, 1, 2, decoded);
        return true;
    }

    private static TxtgFile? Open(AssetContext context, string name, TxtgFormat expected)
    {
        string? path = context.Romfs.TextureFile(name);
        if (path is null || !path.EndsWith(".txtg", StringComparison.OrdinalIgnoreCase)) return null;
        TxtgFile file = TxtgFile.FromBytes(File.ReadAllBytes(Path.Combine(context.Romfs.Root, path)));
        return file.Format == expected ? file : null;
    }

    private static void Red(TextureFormat format, ReadOnlySpan<byte> blocks, int width, int height, Span<byte> destination)
    {
        byte[] rgba = TextureDecoder.ToRgba8(format, blocks.ToArray(), width, height);
        for (int i = 0; i < width * height; i++) destination[i] = rgba[i * 4];
    }

    private static void Store(AssetContext context, string name, int width, int height, int depth, int channels, byte[] texels)
    {
        byte[] bytes = new byte[16 + texels.Length];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), width);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), height);
        BitConverter.TryWriteBytes(bytes.AsSpan(8), depth);
        BitConverter.TryWriteBytes(bytes.AsSpan(12), channels);
        texels.CopyTo(bytes, 16);
        context.Pack.AddEntry(PackEntryKind.GameData, EntryName(name), context.Pack.Add(bytes));
    }
}
