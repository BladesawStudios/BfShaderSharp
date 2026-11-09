using BntxSharp;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using ZsDicSharp;

namespace BfShaderSharp.Assets;

/// <summary>Stores a BNTX texture file and one texture entry for each texture in it.</summary>
/// <remarks>
/// Native storage keeps a compressed file as the zstd frame the game shipped, under the hash of that frame, so no pixel is
/// recompressed and the stored size is the game's own; an uncompressed file is stored Brotli-compressed. Linear storage
/// stores each texture as its own zstd frame of deswizzled levels. Either way the file is decompressed once to read the
/// header of each texture. Returns the number of textures the file holds.
/// </remarks>
public static class TextureImporter
{
    public static int Import(AssetContext context, string path)
    {
        byte[] file = File.ReadAllBytes(path);
        bool compressed = ZsDic.IsCompressed(file);
        byte[] bntx = compressed ? context.Romfs.Dictionaries.Decompress(file, path) : file;
        BntxFile textures = BntxFile.Load(bntx);
        BlobKey native = default;
        if (context.Storage == TextureStorage.Native) native = compressed ? context.Pack.AddFrame(file, bntx.Length, ZsDic.FrameDictionaryId(file)) : context.Pack.Add(bntx);
        for (int i = 0; i < textures.Textures.Count; i++)
        {
            BntxTexture texture = textures.Textures[i];
            TextureRecord record = context.Storage == TextureStorage.Native ? Native(native, i, texture) : LinearTexture.Store(context, i, texture);
            RecordWriter writer = new();
            record.Write(writer);
            BlobKey key = context.Pack.Add(writer.ToArray(), BlobCodec.Raw);
            context.TextureRecords[texture.Name] = key;
            context.Pack.AddEntry(PackEntryKind.Texture, texture.Name, key);
        }

        return textures.Textures.Count;
    }

    private static TextureRecord Native(BlobKey data, int index, BntxTexture texture) =>
        new(data, index, (uint)texture.Format, texture.Width, texture.Height, texture.Depth, texture.ArrayLength, texture.MipCount);
}
