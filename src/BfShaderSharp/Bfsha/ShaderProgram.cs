using BfShaderSharp.Binary;

namespace BfShaderSharp.Bfsha;

/// <summary>The binding slot of a sampler, uniform block or image in the stages of a program; -1 means unused.</summary>
/// <remarks>Read from the per-program location tables of a shading model. Geometry and Compute are -1 in a version that has no slot for them.</remarks>
public readonly record struct StageLocations(int Vertex, int Fragment, int Geometry = -1, int Compute = -1);

/// <summary>One program of a shading model: which compiled variation it uses and where it binds samplers, uniform blocks and images.</summary>
/// <remarks>
/// A program is a fixed-size record whose layout is the archive version's. Its variation is an entry of the model's BNSH, found from the variation offset
/// the record holds. The binding tables are read when asked for, one set of locations for each sampler, image or
/// uniform block of the model.
/// </remarks>
public readonly struct ShaderProgram
{
    private const int VariationTableStart = 192;
    private const int VariationEntrySize = 64;
    private readonly ByteView _view;
    private readonly long _at;
    private readonly long _bnshStart;
    private readonly ProgramLayout _layout;

    internal ShaderProgram(ByteView view, long at, long bnshStart, int index, ProgramLayout layout)
    {
        _view = view;
        _at = at;
        _bnshStart = bnshStart;
        Index = index;
        _layout = layout;
    }

    public int Index { get; }

    public int VariationIndex => (int)(((long)_view.U64(_at + _layout.Variation) - (_bnshStart + VariationTableStart)) / VariationEntrySize);

    public uint UsedAttributeFlags => _view.U32(_at + _layout.UsedAttributes);

    public ushort Flags => _view.U16(_at + _layout.Flags);

    public int SamplerCount => _view.U16(_at + _layout.SamplerCount);

    public int ImageCount => _layout.ImageCount < 0 ? 0 : _view.U16(_at + _layout.ImageCount);

    public int UniformBlockCount => _view.U16(_at + _layout.BlockCount);

    public StageLocations Sampler(int index) => Location(_layout.Samplers, index, SamplerCount);

    public StageLocations Image(int index) => Location(_layout.Images, index, ImageCount);

    public StageLocations UniformBlock(int index) => Location(_layout.Blocks, index, UniformBlockCount);

    private StageLocations Location(int field, int index, int count)
    {
        if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
        long at = (long)_view.U64(_at + field) + (long)index * _layout.LocationSize;
        if (_layout.LocationSize == 8) return new StageLocations(_view.I32(at), _view.I32(at + 4));
        return new StageLocations(_view.I32(at), (sbyte)_view.I32(at + 8), _view.I32(at + 4), (sbyte)_view.I32(at + 12));
    }
}
