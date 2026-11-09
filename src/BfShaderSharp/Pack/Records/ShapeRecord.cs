namespace BfShaderSharp.Pack.Records;

/// <summary>The geometry of a shape: its vertex layout, streams, levels of detail and bounds.</summary>
/// <remarks>
/// The record holds no name, no material and no bone, which belong to the model that uses the shape, so identical geometry
/// is one record however many models draw it. Radii are the bounding sphere radii and BoundingRadii the four-float
/// bounds of the model file; SkinBones are the bone indices a skinned shape's weights refer to.
/// </remarks>
public sealed record ShapeRecord(
    uint VertexCount,
    byte SkinCount,
    IReadOnlyList<AttribRecord> Attribs,
    IReadOnlyList<StreamRecord> Streams,
    IReadOnlyList<MeshRecord> Meshes,
    IReadOnlyList<float> Radii,
    IReadOnlyList<float> BoundingRadii,
    IReadOnlyList<ushort> SkinBones)
{
    public void Write(RecordWriter writer)
    {
        writer.U32(VertexCount);
        writer.U8(SkinCount);
        writer.List(Attribs, (w, v) => v.Write(w));
        writer.List(Streams, (w, v) => v.Write(w));
        writer.List(Meshes, (w, v) => v.Write(w));
        writer.List(Radii, (w, v) => w.F32(v));
        writer.List(BoundingRadii, (w, v) => w.F32(v));
        writer.List(SkinBones, (w, v) => w.U16(v));
    }

    public static ShapeRecord Read(RecordReader reader) =>
        new(reader.U32(), reader.U8(), reader.List(10, AttribRecord.Read), reader.List(18, StreamRecord.Read), reader.List(40, MeshRecord.Read),
            reader.List(4, r => r.F32()), reader.List(4, r => r.F32()), reader.List(2, r => r.U16()));
}
