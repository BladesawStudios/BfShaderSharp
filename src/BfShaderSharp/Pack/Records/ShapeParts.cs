namespace BfShaderSharp.Pack.Records;

/// <summary>One vertex attribute: its name, the format word of the model file, and where it sits in which stream.</summary>
/// <remarks>The format is the GPU attribute format number as the model file stores it, so it is the same word on Switch and Switch 2 files.</remarks>
public sealed record AttribRecord(string Name, uint Format, ushort Offset, ushort Stream)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.U32(Format);
        writer.U16(Offset);
        writer.U16(Stream);
    }

    public static AttribRecord Read(RecordReader reader) => new(reader.String(), reader.U32(), reader.U16(), reader.U16());
}

/// <summary>One interleaved vertex stream: its stride and the blob that holds its bytes.</summary>
/// <remarks>The blob is stored as the file had it, so streams that two shapes or two models share are one blob.</remarks>
public sealed record StreamRecord(ushort Stride, BlobKey Data)
{
    public void Write(RecordWriter writer)
    {
        writer.U16(Stride);
        writer.Key(Data);
    }

    public static StreamRecord Read(RecordReader reader) => new(reader.U16(), reader.Key());
}

/// <summary>One level of detail of a shape: how it is drawn, its sub-meshes and the blob of its indices.</summary>
/// <remarks>Sub-meshes are (offset, count) pairs in indices. IndexFormat is 0 for bytes, 1 for 16-bit and 2 for 32-bit indices.</remarks>
public sealed record MeshRecord(uint Primitive, uint IndexFormat, uint FirstVertex, IReadOnlyList<(uint Offset, uint Count)> SubMeshes, BlobKey Indices, uint IndexCount)
{
    public void Write(RecordWriter writer)
    {
        writer.U32(Primitive);
        writer.U32(IndexFormat);
        writer.U32(FirstVertex);
        writer.List(SubMeshes, (w, s) =>
        {
            w.U32(s.Offset);
            w.U32(s.Count);
        });
        writer.Key(Indices);
        writer.U32(IndexCount);
    }

    public static MeshRecord Read(RecordReader reader) =>
        new(reader.U32(), reader.U32(), reader.U32(), reader.List(8, r => (r.U32(), r.U32())), reader.Key(), reader.U32());
}
