using BntxSharp;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using ZsDicSharp;

namespace BfShaderSharp.Assets;

/// <summary>Stores one texture as deswizzled levels in a zstd frame of its own.</summary>
/// <remarks>
/// Every array slice is written from its largest mip down, each level as plain rows of blocks, so a renderer decompresses
/// once and uploads each level at its offset with no deswizzle. The frame has no dictionary. Identical textures produce
/// identical frames and are stored once.
/// </remarks>
internal static class LinearTexture
{
    internal static TextureRecord Store(AssetContext context, int index, BntxTexture texture)
    {
        List<uint> sizes = [];
        using MemoryStream levels = new();
        for (int slice = 0; slice < texture.ArrayLength; slice++)
        {
            for (int mip = 0; mip < texture.MipCount; mip++)
            {
                byte[] bytes = texture.GetDeswizzledData(mip, slice);
                levels.Write(bytes);
                sizes.Add((uint)bytes.Length);
            }
        }

        byte[] frame = context.Romfs.Dictionaries.Compress(levels.GetBuffer().AsSpan(0, (int)levels.Length), ZsDictionary.None, context.LinearLevel);
        BlobKey key = context.Pack.AddFrame(frame, (int)levels.Length, 0);
        return new TextureRecord(key, 0, (uint)texture.Format, texture.Width, texture.Height, texture.Depth, texture.ArrayLength, texture.MipCount, TextureStorage.Linear, sizes);
    }
}
