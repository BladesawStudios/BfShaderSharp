namespace BfShaderSharp.Pack.Records;

/// <summary>How the bytes behind a texture's file blob are laid out.</summary>
/// <remarks>Native is the BNTX file as the game stored it, with block-linear levels. Linear is the levels alone, deswizzled to plain rows of blocks, array slice by array slice and each slice from its largest mip down, with the size of each level in the record.</remarks>
public enum TextureStorage : byte
{
    Native = 0,
    Linear = 1,
}

/// <summary>Where a texture's pixels are and what they are: the blob that holds them and the shape of the image.</summary>
/// <remarks>
/// A Native texture's blob is the BNTX file as the game stored it, one zstd frame shared by every texture of that file, and
/// Index is the texture's place in it. A Linear texture's blob is its own, and Levels lists the byte size of every level in
/// the blob's order. The format is the BNTX surface format word and the dimensions are of the first mip.
/// </remarks>
public sealed record TextureRecord(BlobKey File, int Index, uint Format, int Width, int Height, int Depth, int ArrayLength, int MipCount, TextureStorage Storage = TextureStorage.Native, IReadOnlyList<uint>? Levels = null)
{
    public IReadOnlyList<uint> LevelSizes => Levels ?? [];

    public void Write(RecordWriter writer)
    {
        writer.Key(File);
        writer.U16((ushort)Index);
        writer.U32(Format);
        writer.U32((uint)Width);
        writer.U32((uint)Height);
        writer.U32((uint)Depth);
        writer.U32((uint)ArrayLength);
        writer.U32((uint)MipCount);
        writer.U8((byte)Storage);
        writer.List(LevelSizes, (w, v) => w.U32(v));
    }

    public static TextureRecord Read(RecordReader reader) =>
        new(reader.Key(), reader.U16(), reader.U32(), (int)reader.U32(), (int)reader.U32(), (int)reader.U32(), (int)reader.U32(), (int)reader.U32(), (TextureStorage)reader.U8(), reader.List(4, r => r.U32()));
}
