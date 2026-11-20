using BfShaderSharp.Binary;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Container;
using BfShaderSharp.Errors;
using BfShaderSharp.Lookup;

namespace BfShaderSharp.Bfsha;

/// <summary>One shading model of a BFSHA: its option tables, program table and the BNSH holding its compiled shaders.</summary>
/// <remarks>Holds a position in the archive and reads fields as they are asked for. The BNSH is a slice of the archive bytes.</remarks>
public readonly struct ShaderModel
{
        private readonly ByteView _view;
    private readonly long _at;
    private readonly BfshaLayout _layout;

    internal ShaderModel(ByteView view, long at, string name, BfshaLayout layout)
    {
        _layout = layout;
        _view = view;
        _at = at;
        Name = name;
    }

    public string Name { get; }

    public NamedTable<ShaderOption> StaticOptions => Table(0x08, ShaderOption.Size, Option);

    public NamedTable<ShaderOption> DynamicOptions => Table(0x18, ShaderOption.Size, Option);

    public NamedTable<ShaderAttribute> Attributes => Table(0x28, ShaderAttribute.Size, ShaderAttribute.Read);

    public NamedTable<ShaderSampler> Samplers => Table(0x38, ShaderSampler.Size, ShaderSampler.Read);

    public ResDict Images => _layout.Images < 0 ? default : Dict(_layout.Images + 8);

    public NamedTable<ShaderUniformBlock> UniformBlocks => Table(_layout.Blocks, ShaderUniformBlock.Size, ShaderUniformBlock.Read);

    public int DefaultProgramIndex => _view.I32(_at + _layout.DefaultProgram);

    public int StaticOptionCount => _view.U16(_at + _layout.StaticOptionCount);

    public int DynamicOptionCount => _view.U16(_at + _layout.DynamicOptionCount);

    public int ProgramCount => _view.U16(_at + _layout.ProgramCount);

    public int StaticKeyLength => _view.U8(_at + _layout.StaticKeyLength);

    public int DynamicKeyLength => _view.U8(_at + _layout.DynamicKeyLength);

    public ShaderProgram Program(int index)
    {
        if ((uint)index >= (uint)ProgramCount) throw new ArgumentOutOfRangeException(nameof(index));
        long programs = (long)_view.U64(_at + _layout.Programs);
        return new ShaderProgram(_view, programs + (long)index * _layout.Program.Size, (long)_view.U64(_at + _layout.Bnsh), index, _layout.Program);
    }

    // One row of 32-bit key words for each program: the static words, then the dynamic words.
    public ReadOnlyMemory<byte> KeyRows => _view.Bytes((long)_view.U64(_at + _layout.Keys), (long)ProgramCount * RowWords * 4);

    public int RowWords => StaticKeyLength + DynamicKeyLength;

    public ProgramLookup CreateLookup() => new(Options(), KeyRows, RowWords, ProgramCount);

    // The options as the lookup sees them: a static option keys the word at its index, a dynamic option's word follows the static words, less its key offset.
    public IReadOnlyList<LookupOption> Options()
    {
        List<LookupOption> options = [];
        AddOptions(options, StaticOptions, isStatic: true);
        AddOptions(options, DynamicOptions, isStatic: false);
        return options;
    }

    public BnshFile Bnsh
    {
        get
        {
            long start = (long)_view.U64(_at + _layout.Bnsh);
            if (start == 0) throw new BfShaderFormatException("Shading model has no shader binary", _at + _layout.Bnsh);
            return BnshFile.Read(_view.Bytes(start, _view.U32(start + 0x1C)));
        }
    }

    private void AddOptions(List<LookupOption> options, NamedTable<ShaderOption> table, bool isStatic)
    {
        for (int i = 0; i < table.Count; i++)
        {
            ShaderOption option = table[i];
            int word = isStatic ? option.KeyIndex : StaticKeyLength + option.KeyIndex - option.KeyOffset;
            options.Add(new LookupOption(option.Name, word, option.Mask, option.Shift, option.DefaultChoice, [.. option.Choices.Keys()]));
        }
    }

    private ShaderOption Option(ByteView view, long at, string name) => new(view, at, name, _layout.Option);

    private ResDict Dict(int field) => new(_view, (long)_view.U64(_at + field));

    // A table's values pointer is at the field and its name dictionary 8 bytes later.
    private NamedTable<T> Table<T>(int field, int stride, Func<ByteView, long, string, T> make) =>
        new(_view, (long)_view.U64(_at + field), stride, Dict(field + 8), make);
}
