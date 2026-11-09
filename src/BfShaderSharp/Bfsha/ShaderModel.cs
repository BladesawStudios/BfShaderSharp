using BfShaderSharp.Binary;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Container;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Bfsha;

/// <summary>One shading model of a BFSHA: its option tables, program table and the BNSH holding its compiled shaders.</summary>
/// <remarks>Holds a position in the archive and reads fields as they are asked for. The BNSH is a slice of the archive bytes.</remarks>
public readonly struct ShaderModel
{
    internal const int Size = 0x100;
    private readonly ByteView _view;
    private readonly long _at;

    internal ShaderModel(ByteView view, long at, string name)
    {
        _view = view;
        _at = at;
        Name = name;
    }

    public string Name { get; }

    public OptionTable StaticOptions => Options(0x08);

    public OptionTable DynamicOptions => Options(0x18);

    public ResDict Attributes => Dict(0x30);

    public ResDict Samplers => Dict(0x40);

    public ResDict Images => Dict(0x50);

    public ResDict UniformBlocks => Dict(0x60);

    public int DefaultProgramIndex => _view.I32(_at + 0xE0);

    public int StaticOptionCount => _view.U16(_at + 0xE4);

    public int DynamicOptionCount => _view.U16(_at + 0xE6);

    public int ProgramCount => _view.U16(_at + 0xE8);

    public int StaticKeyLength => _view.U8(_at + 0xEA);

    public int DynamicKeyLength => _view.U8(_at + 0xEB);

    public ShaderProgram Program(int index)
    {
        if ((uint)index >= (uint)ProgramCount) throw new ArgumentOutOfRangeException(nameof(index));
        long programs = (long)_view.U64(_at + 0x88);
        return new ShaderProgram(_view, programs + (long)index * ShaderProgram.Size, (long)_view.U64(_at + 0xA8), index);
    }

    /// <summary>One row of 32-bit key words for each program: the static words, then the dynamic words.</summary>
    internal ReadOnlyMemory<byte> KeyTable =>
        _view.Bytes((long)_view.U64(_at + 0x90), (long)ProgramCount * (StaticKeyLength + DynamicKeyLength) * 4);

    public ProgramLookup CreateLookup() => new(this);

    public BnshFile Bnsh
    {
        get
        {
            long start = (long)_view.U64(_at + 0xA8);
            if (start == 0) throw new BfShaderFormatException("Shading model has no shader binary", _at + 0xA8);
            return BnshFile.Read(_view.Bytes(start, _view.U32(start + 0x1C)));
        }
    }

    private ResDict Dict(int field) => new(_view, (long)_view.U64(_at + field));

    // An option table's values pointer is at the field and its name dictionary 8 bytes later.
    private OptionTable Options(int field) => new(_view, (long)_view.U64(_at + field), Dict(field + 8));
}
