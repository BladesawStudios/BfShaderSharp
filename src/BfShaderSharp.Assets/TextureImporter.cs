using BntxSharp;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using ZsDicSharp;

namespace BfShaderSharp.Assets;

/// <summary>Stores a BNTX texture file and one texture entry for each texture in it.</summary>
/// <remarks>
/// A compressed file is stored as the zstd frame the game shipped, under the hash of that frame, so no pixel is
/// decompressed to be stored and the stored size is the game's own. The file is decompressed once to read the header of
/// each texture. An uncompressed file is stored Brotli-compressed. Returns the number of textures the file holds.
/// </remarks>
public static class TextureImporter
{
    public static int Import(AssetContext context, string path)
    {
        byte[] file = File.ReadAllBytes(path);
        bool compressed = ZsDic.IsCompressed(file);
        byte[] bntx = compressed ? context.Romfs.Dictionaries.Decompress(file, path) : file;
        BntxFile textures = BntxFile.Load(bntx);
        BlobKey data = compressed ? context.Pack.AddFrame(file, bntx.Length, ZsDic.FrameDictionaryId(file)) : context.Pack.Add(bntx);
        for (int i = 0; i < textures.Textures.Count; i++)
        {
            BntxTexture texture = textures.Textures[i];
            RecordWriter writer = new();
            new TextureRecord(data, i, (uint)texture.Format, texture.Width, texture.Height, texture.Depth, texture.ArrayLength, texture.MipCount).Write(writer);
            context.Pack.AddEntry(PackEntryKind.Texture, texture.Name, context.Pack.Add(writer.ToArray(), BlobCodec.Raw));
        }

        return textures.Textures.Count;
    }
}
