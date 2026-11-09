using BfShaderSharp.Binary;
using BfShaderSharp.Container;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Bnsh;

/// <summary>A BNSH shader binary: a table of compiled shader variations.</summary>
/// <remarks>
/// Reading parses the two headers and checks the variation table fits; programs, stage code and reflection are
/// read when asked for and are slices of the source bytes. A BNSH inside a BFSHA is read from a view that starts
/// at the BNSH, since every offset in it is relative to its own start.
/// </remarks>
public sealed class BnshFile
{
    private const string Magic = "BNSH";
    private const string BlockMagic = "grsc";
    private readonly ByteView _view;
    private readonly long _variationStart;

    private BnshFile(ByteView view, ResHeader header, long blockOffset)
    {
        _view = view;
        Header = header;
        ApiType = view.U16(blockOffset + 0x10);
        ApiVersion = view.U16(blockOffset + 0x12);
        CodeTarget = view.U32(blockOffset + 0x14);
        CompilerVersion = view.U32(blockOffset + 0x18);
        VariationCount = checked((int)view.U32(blockOffset + 0x1C));
        _variationStart = (long)view.U64(blockOffset + 0x20);
        if (_variationStart + (long)VariationCount * BnshVariation.EntrySize > view.Length)
            throw new BfShaderFormatException($"Variation table of {VariationCount} entries runs past the file", _variationStart);
        Name = header.NameOffset == 0 ? "" : view.String(header.NameOffset - 2);
    }

    public ResHeader Header { get; }

    public string Name { get; }

    public ushort ApiType { get; }

    public ushort ApiVersion { get; }

    public uint CodeTarget { get; }

    public uint CompilerVersion { get; }

    public int VariationCount { get; }

    public ReadOnlyMemory<byte> Bytes => _view.Memory;

    public static BnshFile Read(ReadOnlyMemory<byte> bytes)
    {
        ByteView view = new(bytes);
        ResHeader header = ResHeader.Read(view, Magic);
        long block = header.BlockOffset;
        if (!view.StartsWith(block, "grsc"u8)) throw new BfShaderFormatException($"Expected the {BlockMagic} block", block);
        return new BnshFile(view, header, block);
    }

    public BnshVariation Variation(int index)
    {
        if ((uint)index >= (uint)VariationCount) throw new ArgumentOutOfRangeException(nameof(index));
        return new BnshVariation(_view, _variationStart + (long)index * BnshVariation.EntrySize, index);
    }

    public IEnumerable<BnshVariation> Variations()
    {
        for (int i = 0; i < VariationCount; i++) yield return Variation(i);
    }
}
