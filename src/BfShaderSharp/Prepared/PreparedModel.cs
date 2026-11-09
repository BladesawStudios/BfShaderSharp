using BfShaderSharp.Binary;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Errors;
using BfShaderSharp.Lookup;

namespace BfShaderSharp.Prepared;

/// <summary>One shading model of a prepared file.</summary>
/// <remarks>
/// Holds the position of the model record and reads tables when asked. <see cref="CreateLookup"/> returns the same lookup
/// an archive gives, built from the stored options and key rows. Program and variation records are read in place.
/// </remarks>
public sealed class PreparedModel
{
    private readonly PreparedArchive _archive;
    private readonly ByteView _view;
    private readonly long _at;

    internal PreparedModel(PreparedArchive archive, ByteView view, long at)
    {
        _archive = archive;
        _view = view;
        _at = at;
        Name = archive.String(checked((int)view.U32(at)));
        ProgramCount = Count(1);
        RowWords = Count(2);
        VariationCount = Count(4);
        SamplerCount = Count(5);
        BlockCount = Count(7);
    }

    public string Name { get; }

    public int ProgramCount { get; }

    public int RowWords { get; }

    public int VariationCount { get; }

    public int SamplerCount { get; }

    public int BlockCount { get; }

    public PreparedProgram Program(int index)
    {
        if ((uint)index >= (uint)ProgramCount) throw new ArgumentOutOfRangeException(nameof(index));
        int slots = SamplerCount + BlockCount;
        long variation = Table(3) + (long)index * 4;
        return new PreparedProgram(_view, (int)_view.U32(variation), Table(4) + (long)index * slots * 4, SamplerCount);
    }

    /// <summary>The blob index of a stage of a variation, or -1 when the variation has no such stage.</summary>
    public int StageBlob(int variation, ShaderStage stage)
    {
        if ((uint)variation >= (uint)VariationCount) throw new ArgumentOutOfRangeException(nameof(variation));
        return _view.I32(Table(2) + (long)variation * PreparedLayout.VariationSize + (long)stage * 4);
    }

    public string? Glsl(int program, ShaderStage stage)
    {
        int blob = StageBlob(Program(program).Variation, stage);
        return blob < 0 ? null : _archive.Glsl(blob);
    }

    public IReadOnlyList<PreparedSampler> Samplers() =>
        Read(SamplerCount, Table(5), PreparedLayout.SamplerSize, at => new PreparedSampler(Str(at), Str(at + 4), _view.I32(at + 8)));

    public IReadOnlyList<PreparedAttribute> Attributes() =>
        Read(Count(6), Table(6), PreparedLayout.AttributeSize, at => new PreparedAttribute(Str(at), _view.I32(at + 4), _view.I32(at + 8)));

    public IReadOnlyList<PreparedBlock> Blocks() => Read(BlockCount, Table(7), PreparedLayout.BlockSize, ReadBlock);

    public ProgramLookup CreateLookup()
    {
        var options = Read(Count(3), Table(0), PreparedLayout.OptionSize, ReadOption);
        return new ProgramLookup(options, _view.Bytes(Table(1), (long)ProgramCount * RowWords * 4), RowWords, ProgramCount);
    }

    private LookupOption ReadOption(long at)
    {
        int count = checked((int)_view.U32(at + 16));
        long choices = (long)_view.U64(at + 24);
        string[] names = new string[count];
        for (int i = 0; i < count; i++) names[i] = _archive.String(checked((int)_view.U32(choices + (long)i * 4)));
        return new LookupOption(Str(at), _view.U16(at + 8), _view.U32(at + 4), _view.U8(at + 10), _view.I32(at + 12), names);
    }

    private PreparedBlock ReadBlock(long at)
    {
        long uniformsAt = (long)_view.U64(at + 16);
        PreparedUniform[] uniforms = Read(checked((int)_view.U32(at + 12)), uniformsAt, PreparedLayout.UniformSize,
            u => new PreparedUniform(Str(u), _view.I32(u + 4), (int)_view.U32(u + 8), (int)_view.U32(u + 12))).ToArray();
        return new PreparedBlock(Str(at), _view.U16(at + 4), _view.U16(at + 6), (int)_view.U32(at + 8), uniforms, _view.I32(at + 24));
    }

    private string Str(long at) => _archive.String(checked((int)_view.U32(at)));

    private int Count(int field) => checked((int)_view.U32(_at + field * 4L));

    private long Table(int field) => checked((long)_view.U64(_at + 32 + field * 8L));

    private static List<T> Read<T>(int count, long start, int stride, Func<long, T> read)
    {
        List<T> items = new(count);
        for (int i = 0; i < count; i++) items.Add(read(start + (long)i * stride));
        return items;
    }
}
