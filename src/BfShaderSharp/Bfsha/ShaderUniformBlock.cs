using BfShaderSharp.Binary;
using BfShaderSharp.Container;

namespace BfShaderSharp.Bfsha;

/// <summary>A variable inside a uniform block: its name and its byte offset in the block.</summary>
/// <remarks>Read from the uniform table of a uniform block.</remarks>
public readonly record struct ShaderUniform(string Name, int Index, int Offset, int BlockIndex)
{
    internal const int Size = 0x10;

    internal static ShaderUniform Read(ByteView view, long at, string name) =>
        new(name, view.I32(at + 8), view.U16(at + 0xC), view.U8(at + 0xE));
}

/// <summary>The kind of data a uniform block holds.</summary>
public enum UniformBlockType
{
    None,
    Material,
    Shape,
    Option,
}

/// <summary>A uniform block of a shading model: its size, kind, variables and default contents.</summary>
/// <remarks>The variables and the default buffer are read when asked for; the default buffer is a slice of the file bytes.</remarks>
public readonly struct ShaderUniformBlock
{
    internal const int Size = 0x20;
    private readonly ByteView _view;
    private readonly long _at;

    internal ShaderUniformBlock(ByteView view, long at, string name)
    {
        _view = view;
        _at = at;
        Name = name;
    }

    public string Name { get; }

    public int Index => _view.U8(_at + 0x18);

    public UniformBlockType Type => (UniformBlockType)_view.U8(_at + 0x19);

    public int ByteSize => _view.U16(_at + 0x1A);

    public int UniformCount => _view.U16(_at + 0x1C);

    public NamedTable<ShaderUniform> Uniforms =>
        new(_view, (long)_view.U64(_at), ShaderUniform.Size, new ResDict(_view, (long)_view.U64(_at + 8)), ShaderUniform.Read);

    public ReadOnlyMemory<byte> DefaultBuffer => _view.U64(_at + 0x10) is var offset and not 0 ? _view.Bytes((long)offset, ByteSize) : default;

    internal static ShaderUniformBlock Read(ByteView view, long at, string name) => new(view, at, name);
}
