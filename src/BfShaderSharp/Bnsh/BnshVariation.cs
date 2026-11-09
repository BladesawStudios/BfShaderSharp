using BfShaderSharp.Binary;

namespace BfShaderSharp.Bnsh;

/// <summary>One entry of a BNSH's variation table, which leads to a shader program binary.</summary>
/// <remarks>Holds only the table position; <see cref="Program"/> reads the program when it is asked for.</remarks>
public readonly struct BnshVariation
{
    internal const int EntrySize = 0x40;
    private readonly ByteView _bnsh;
    private readonly long _entry;

    internal BnshVariation(ByteView bnsh, long entry, int index)
    {
        _bnsh = bnsh;
        _entry = entry;
        Index = index;
    }

    public int Index { get; }

    // The file offset of the variation's table entry.
    public long Offset => _entry;

    public ShaderProgramBinary Program => new(_bnsh, (long)_bnsh.U64(_entry + 0x10));
}
