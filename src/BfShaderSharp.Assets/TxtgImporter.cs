using BntxSharp;
using BfShaderSharp.Errors;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using TxtgSharp;
using ZsDicSharp;

namespace BfShaderSharp.Assets;

/// <summary>Stores a Tears of the Kingdom TexToGo (.txtg) texture as one linear texture entry named by its file.</summary>
/// <remarks>
/// A TXTG file holds one texture whose levels are already plain rows of blocks, so it is stored in the linear layout
/// of <see cref="LinearTexture"/> whatever the storage option says, with its format expressed as the BNTX surface format
/// word and its channel selectors as BNTX channel sources, so a renderer reads it like any other texture.
/// </remarks>
public static class TxtgImporter
{
    public static int Import(AssetContext context, string path)
    {
        TxtgFile file = TxtgFile.FromBytes(File.ReadAllBytes(path));
        List<uint> sizes = [];
        using MemoryStream levels = new();
        for (int layer = 0; layer < file.LayerCount; layer++)
        {
            for (int mip = 0; mip < file.MipCount; mip++)
            {
                byte[] bytes = (file.Surface(layer, mip) ?? throw new BfShaderFormatException($"{path} has no surface for layer {layer} mip {mip}.")).Data;
                levels.Write(bytes);
                sizes.Add((uint)bytes.Length);
            }
        }

        byte[] frame = context.Romfs.Dictionaries.Compress(levels.GetBuffer().AsSpan(0, (int)levels.Length), ZsDictionary.None, context.LinearLevel);
        BlobKey key = context.Pack.AddFrame(frame, (int)levels.Length, 0);
        TextureRecord record = new(key, 0, SurfaceFormatOf(file, path), file.Width, file.Height, 1, file.LayerCount, file.MipCount, TextureStorage.Linear, sizes, ChannelsOf(file));
        RecordWriter writer = new();
        record.Write(writer);
        BlobKey recordKey = context.Pack.Add(writer.ToArray(), BlobCodec.Raw);
        string name = Path.GetFileNameWithoutExtension(path);
        context.TextureRecords[name] = recordKey;
        context.Pack.AddEntry(PackEntryKind.Texture, name, recordKey);
        return 1;
    }

    private static uint ChannelsOf(TxtgFile file)
    {
        (byte r, byte g, byte b, byte a) = file.ChannelSelectors;
        return Source(r) | Source(g) << 8 | Source(b) << 16 | Source(a) << 24;
    }

    // TXTG numbers the sources red 0 to alpha 3, zero 4 and one 5; BNTX numbers them zero 0, one 1, red 2 to alpha 5.
    private static uint Source(byte selector) => selector switch { < 4 => (uint)selector + 2, 4 => 0, _ => 1 };

    private static uint SurfaceFormatOf(TxtgFile file, string path)
    {
        bool srgb = file.Format.IsSrgb();
        uint variant = srgb ? 6u : 1u;
        return file.Format switch
        {
            TxtgFormat.Bc1Unorm or TxtgFormat.Bc1UnormSrgb => 0x1A00 | variant,
            TxtgFormat.Bc3UnormSrgb => 0x1C00 | variant,
            TxtgFormat.Bc4Unorm => 0x1D01,
            TxtgFormat.Bc5Unorm => 0x1E01,
            TxtgFormat.Bc7Unorm => 0x2000 | variant,
            TxtgFormat.R8Unorm => 0x0201,
            TxtgFormat.R8G8Unorm => 0x0901,
            TxtgFormat.R8G8B8A8Unorm => 0x0B01,
            _ when file.Format.IsAstc() => AstcFamily(file.BlockInfo.Width, file.BlockInfo.Height, path) | variant,
            _ => throw new BfShaderUnsupportedException($"{path} has the TXTG format {file.RawFormatCode:X}, which has no surface format."),
        };
    }

    private static uint AstcFamily(int width, int height, string path) => (width, height) switch
    {
        (4, 4) => 0x2D00, (5, 4) => 0x2E00, (5, 5) => 0x2F00, (6, 5) => 0x3000, (6, 6) => 0x3100, (8, 5) => 0x3200,
        (8, 6) => 0x3300, (8, 8) => 0x3400, (10, 5) => 0x3500, (10, 6) => 0x3600, (10, 8) => 0x3700, (10, 10) => 0x3800,
        (12, 10) => 0x3900, (12, 12) => 0x3A00,
        _ => throw new BfShaderUnsupportedException($"{path} has the ASTC footprint {width}x{height}."),
    };
}
