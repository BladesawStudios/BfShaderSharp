namespace BfShaderSharp.Pack.Records;

/// <summary>A uniform block's size and the byte offset of each named uniform in it.</summary>
/// <remarks>Every material of a shading model has the same layout, so a pack stores it once. Animations of material parameters address a uniform by name and a byte offset inside it, which this places in the block.</remarks>
public sealed record UniformLayoutRecord(string Name, int Size, IReadOnlyList<UniformOffset> Uniforms)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.I32(Size);
        writer.List(Uniforms, (w, v) =>
        {
            w.String(v.Name);
            w.I32(v.Offset);
        });
    }

    public static UniformLayoutRecord Read(RecordReader reader) =>
        new(reader.String(), reader.I32(), reader.List(6, r => new UniformOffset(r.String(), r.I32())));

    /// <summary>The offset of a uniform by name, or -1.</summary>
    public int OffsetOf(string name) => Uniforms.FirstOrDefault(u => u.Name == name) is { Name: not null } found ? found.Offset : -1;
}

/// <summary>A uniform's name and its byte offset in the block.</summary>
public readonly record struct UniformOffset(string Name, int Offset);
