using BfShaderSharp.Binary;

namespace BfShaderSharp.Bfsha;

/// <summary>The binding slot of a sampler, uniform block or image in the vertex and fragment stages of a program; -1 means unused.</summary>
/// <remarks>Read from the per-program location tables of a shading model.</remarks>
public readonly record struct StageLocations(int Vertex, int Fragment);

/// <summary>One program of a shading model: which compiled variation it uses and where it binds samplers, uniform blocks and images.</summary>
/// <remarks>
/// A program is a 0x40-byte record. Its variation is an entry of the model's BNSH, found from the variation offset
/// the record holds. The binding tables are read when asked for, one pair of locations for each sampler, image or
/// uniform block of the model.
/// </remarks>
public readonly struct ShaderProgram
{
    internal const int Size = 0x40;
    private const int VariationTableStart = 192;
    private const int VariationEntrySize = 64;
    private readonly ByteView _view;
    private readonly long _at;
    private readonly long _bnshStart;

    internal ShaderProgram(ByteView view, long at, long bnshStart, int index)
    {
        _view = view;
        _at = at;
        _bnshStart = bnshStart;
        Index = index;
    }

    public int Index { get; }

    public int VariationIndex => (int)(((long)_view.U64(_at + 0x20) - (_bnshStart + VariationTableStart)) / VariationEntrySize);

    public uint UsedAttributeFlags => _view.U32(_at + 0x30);

    public ushort Flags => _view.U16(_at + 0x34);

    public int SamplerCount => _view.U16(_at + 0x36);

    public int ImageCount => _view.U16(_at + 0x38);

    public int UniformBlockCount => _view.U16(_at + 0x3A);

    public StageLocations Sampler(int index) => Location(0x00, index, SamplerCount);

    public StageLocations Image(int index) => Location(0x08, index, ImageCount);

    public StageLocations UniformBlock(int index) => Location(0x10, index, UniformBlockCount);

    private StageLocations Location(int field, int index, int count)
    {
        if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
        long at = (long)_view.U64(_at + field) + (long)index * 8;
        return new StageLocations(_view.I32(at), _view.I32(at + 4));
    }
}
