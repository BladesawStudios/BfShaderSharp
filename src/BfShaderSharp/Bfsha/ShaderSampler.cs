using BfShaderSharp.Binary;

namespace BfShaderSharp.Bfsha;

/// <summary>A texture sampler of a shading model: its name, the annotation the tools gave it and its index.</summary>
public readonly record struct ShaderSampler(string Name, string Annotation, int Index)
{
    internal const int Size = 0x10;

    internal static ShaderSampler Read(ByteView view, long at, string name) =>
        new(name, view.String((long)view.U64(at)), view.U8(at + 8));
}

/// <summary>A vertex attribute of a shading model and the input location it binds to; -1 when unused.</summary>
public readonly record struct ShaderAttribute(string Name, int Index, int Location)
{
    internal const int Size = 2;

    internal static ShaderAttribute Read(ByteView view, long at, string name) =>
        new(name, view.U8(at), (sbyte)view.U8(at + 1));
}
