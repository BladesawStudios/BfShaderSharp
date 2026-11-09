namespace BfShaderSharp.Pack.Records;

/// <summary>One render info of a material: a name and its values, which are all integers, all floats or all strings.</summary>
/// <remarks>Kind is 0 for integers, 1 for floats and 2 for strings, the same as the render info types of the model file; the values of the other two kinds are empty.</remarks>
public sealed record RenderInfoRecord(string Name, byte Kind, int[] Ints, float[] Floats, string[] Strings)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.U8(Kind);
        writer.List(Ints, (w, v) => w.I32(v));
        writer.List(Floats, (w, v) => w.F32(v));
        writer.List(Strings, (w, v) => w.String(v));
    }

    public static RenderInfoRecord Read(RecordReader reader) =>
        new(reader.String(), reader.U8(), [.. reader.List(4, r => r.I32())], [.. reader.List(4, r => r.F32())], [.. reader.List(2, r => r.String())]);
}

/// <summary>A sampler of a material: its state and the name of the texture it samples.</summary>
/// <remarks>The texture is named, not referenced by key, so two materials that differ only in where the texture file lives are the same material. The name is looked up in the pack's texture entries.</remarks>
public sealed record SamplerRecord(string Name, string Texture, byte WrapU, byte WrapV, byte WrapW, byte Compare, byte Border, byte Anisotropy, ushort FilterFlags, float MinLod, float MaxLod, float LodBias)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.String(Texture);
        foreach (byte value in (byte[])[WrapU, WrapV, WrapW, Compare, Border, Anisotropy]) writer.U8(value);
        writer.U16(FilterFlags);
        writer.F32(MinLod);
        writer.F32(MaxLod);
        writer.F32(LodBias);
    }

    public static SamplerRecord Read(RecordReader reader) =>
        new(reader.String(), reader.String(), reader.U8(), reader.U8(), reader.U8(), reader.U8(), reader.U8(), reader.U8(), reader.U16(), reader.F32(), reader.F32(), reader.F32());
}

/// <summary>A shader parameter of a material: its name, type and where its value sits in the material's parameter data.</summary>
/// <remarks>Type is the model file's shader parameter type number.</remarks>
public sealed record ParamRecord(string Name, byte Type, ushort Offset)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.U8(Type);
        writer.U16(Offset);
    }

    public static ParamRecord Read(RecordReader reader) => new(reader.String(), reader.U8(), reader.U16());
}

/// <summary>A pair of names: a shader option and its choice, or a shader sampler and the material sampler that feeds it.</summary>
/// <remarks>Both sides are stored as strings; a list of pairs is written in the order it is given.</remarks>
public sealed record NamePair(string Key, string Value)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Key);
        writer.String(Value);
    }

    public static NamePair Read(RecordReader reader) => new(reader.String(), reader.String());
}
