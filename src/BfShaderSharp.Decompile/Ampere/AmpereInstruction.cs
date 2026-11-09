namespace BfShaderSharp.Decompile.Ampere;

/// <summary>One 128-bit Ampere instruction as four little-endian words, with bit-field access.</summary>
/// <remarks>
/// Fields are addressed by bit offset and width as in the instruction encoding tables. The words can be rewritten,
/// which the lowering does to a copy of the source instruction when it substitutes registers. Ported from the Eden
/// emulator's lowering (GPL-3.0-or-later).
/// </remarks>
public struct AmpereInstruction
{
    public uint W0;
    public uint W1;
    public uint W2;
    public uint W3;

    public AmpereInstruction(ReadOnlySpan<byte> bytes)
    {
        W0 = BitConverter.ToUInt32(bytes[..4]);
        W1 = BitConverter.ToUInt32(bytes[4..8]);
        W2 = BitConverter.ToUInt32(bytes[8..12]);
        W3 = BitConverter.ToUInt32(bytes[12..16]);
    }

    public uint this[int word]
    {
        readonly get => word switch { 0 => W0, 1 => W1, 2 => W2, _ => W3 };
        set
        {
            switch (word)
            {
                case 0: W0 = value; break;
                case 1: W1 = value; break;
                case 2: W2 = value; break;
                default: W3 = value; break;
            }
        }
    }

    public readonly uint Bits(int offset, int count)
    {
        int word = offset / 32;
        ulong bits = this[word];
        if (word + 1 < 4) bits |= (ulong)this[word + 1] << 32;
        return (uint)((bits >> (offset % 32)) & ((1UL << count) - 1));
    }

    public readonly uint Opcode => Bits(0, 12);
}
