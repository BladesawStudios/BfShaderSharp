namespace BfShaderSharp.Pack.Records;

/// <summary>One bone: its name, its place in the hierarchy, its matrix slots and its local transform.</summary>
/// <remarks>Parent and the three matrix indices are -1 when the bone has none. Scale and Position are three floats and Rotation four, the quaternion of the model file.</remarks>
public sealed record BoneRecord(string Name, int Parent, uint Flags, int Smooth, int Rigid, int Billboard, float[] Scale, float[] Rotation, float[] Position)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.I32(Parent);
        writer.U32(Flags);
        writer.I32(Smooth);
        writer.I32(Rigid);
        writer.I32(Billboard);
        foreach (float value in Scale.Concat(Rotation).Concat(Position)) writer.F32(value);
    }

    public static BoneRecord Read(RecordReader reader)
    {
        string name = reader.String();
        int parent = reader.I32();
        uint flags = reader.U32();
        int smooth = reader.I32(), rigid = reader.I32(), billboard = reader.I32();
        float[] values = [.. Enumerable.Range(0, 10).Select(_ => reader.F32())];
        return new BoneRecord(name, parent, flags, smooth, rigid, billboard, values[..3], values[3..7], values[7..]);
    }
}

/// <summary>A skeleton: its bones, the map from matrix slots to bones and the inverse model matrices of the skinned slots.</summary>
/// <remarks>Each inverse matrix is the twelve floats of a 3x4 matrix, row by row. A skeleton that two models share is one record.</remarks>
public sealed record SkeletonRecord(uint Flags, IReadOnlyList<BoneRecord> Bones, IReadOnlyList<ushort> MatrixToBone, IReadOnlyList<float[]> InverseMatrices)
{
    public void Write(RecordWriter writer)
    {
        writer.U32(Flags);
        writer.List(Bones, (w, v) => v.Write(w));
        writer.List(MatrixToBone, (w, v) => w.U16(v));
        writer.List(InverseMatrices, (w, m) =>
        {
            foreach (float value in m) w.F32(value);
        });
    }

    public static SkeletonRecord Read(RecordReader reader) =>
        new(reader.U32(), reader.List(50, BoneRecord.Read), reader.List(2, r => r.U16()), reader.List(48, r => (float[])[.. Enumerable.Range(0, 12).Select(_ => r.F32())]));
}
