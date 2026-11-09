namespace BfShaderSharp.Pack.Records;

/// <summary>A shape as one model uses it: its name, the material and bone it is bound to, and the geometry record.</summary>
/// <remarks>MaterialIndex is a place in the model's material list.</remarks>
public sealed record ShapeRef(string Name, ushort MaterialIndex, ushort BoneIndex, BlobKey Shape)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.U16(MaterialIndex);
        writer.U16(BoneIndex);
        writer.Key(Shape);
    }

    public static ShapeRef Read(RecordReader reader) => new(reader.String(), reader.U16(), reader.U16(), reader.Key());
}

/// <summary>A material as one model names it: the name and the material record.</summary>
/// <remarks>The name is the model's; the record behind the key is shared by every model with an identical material.</remarks>
public sealed record MaterialRef(string Name, BlobKey Material)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.Key(Material);
    }

    public static MaterialRef Read(RecordReader reader) => new(reader.String(), reader.Key());
}

/// <summary>A model: its skeleton, its shapes and its materials, each by the key of the record that holds it.</summary>
/// <remarks>
/// Reading one model costs one small record; the geometry, materials and skeleton are read only when asked for, and a
/// piece that other models share is stored once. A model that has no bones has the skeleton key of an empty skeleton.
/// </remarks>
public sealed record ModelRecord(string Name, uint Flags, uint VertexCount, BlobKey Skeleton, IReadOnlyList<ShapeRef> Shapes, IReadOnlyList<MaterialRef> Materials)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.U32(Flags);
        writer.U32(VertexCount);
        writer.Key(Skeleton);
        writer.List(Shapes, (w, v) => v.Write(w));
        writer.List(Materials, (w, v) => v.Write(w));
    }

    public static ModelRecord Read(RecordReader reader) =>
        new(reader.String(), reader.U32(), reader.U32(), reader.Key(), reader.List(22, ShapeRef.Read), reader.List(18, MaterialRef.Read));
}
