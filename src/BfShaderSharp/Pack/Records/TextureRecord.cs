namespace BfShaderSharp.Pack.Records;

/// <summary>Where a texture's pixels are and what they are: the file blob that holds them and the shape of the image.</summary>
/// <remarks>
/// The pixels are the BNTX file as the game stored it, one zstd frame shared by every texture of that file; Index is the
/// texture's place in it. The format is the BNTX surface format word. Dimensions are of the first mip, and the levels
/// follow the file's own block-linear layout, which the BNTX header of the decompressed file describes.
/// </remarks>
public sealed record TextureRecord(BlobKey File, int Index, uint Format, int Width, int Height, int Depth, int ArrayLength, int MipCount)
{
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
    }

    public static TextureRecord Read(RecordReader reader) =>
        new(reader.Key(), reader.U16(), reader.U32(), (int)reader.U32(), (int)reader.U32(), (int)reader.U32(), (int)reader.U32(), (int)reader.U32());
}
