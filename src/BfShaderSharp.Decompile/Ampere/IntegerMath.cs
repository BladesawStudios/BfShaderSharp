namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Integer operations that Maxwell has no single instruction for, built from several.</summary>
/// <remarks>
/// A 32-bit multiply-add is four XMAD half-word steps; a 64-bit right shift is a funnel of two shifts selected by
/// whether the amount reaches 32. Results land in the scratch registers R250 and R251 unless a target is given.
/// Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal sealed class IntegerMath(InstructionContext c)
{
    public void Xmad(uint target, uint a, uint b, uint third, bool aHigh, bool bHigh, uint mode, bool immediate)
    {
        ulong second = immediate ? (b >> (bHigh ? 16 : 0)) & 0xffff : b;
        c.Emit((immediate ? 0x3600000000000000UL : 0x5b00000000000000UL) | c.Predicate | target | ((ulong)a << 8) | (second << 20) |
               ((!immediate && bHigh ? 1UL : 0UL) << 35) | ((ulong)third << 39) | ((ulong)mode << 50) | ((aHigh ? 1UL : 0UL) << 53));
    }

    public void MultiplyLow(uint a, uint b, uint third, bool immediate)
    {
        Xmad(250, a, b, third, false, false, 0, immediate);
        Xmad(251, a, b, 255, true, false, 0, immediate);
        Xmad(251, a, b, 251, false, true, 0, immediate);
        c.Emit(c.Base(0x5c18000000000000UL) | (251UL << 8) | (250UL << 20) | (16UL << 39));
    }

    public void RightShiftU64Low(uint target, uint low, uint high, uint shift)
    {
        c.MovImm(250, 32);
        c.Emit(0x5c10000000000000UL | c.Predicate | 251 | (250UL << 8) | ((ulong)shift << 20) | (1UL << 48));
        c.Emit(0x5c28000000000000UL | c.Predicate | 250 | ((ulong)low << 8) | ((ulong)shift << 20));
        c.Emit(0x5c48000000000000UL | c.Predicate | 251 | ((ulong)high << 8) | (251UL << 20));
        c.Emit(0x5c40000000000000UL | c.Predicate | 250 | (250UL << 8) | (251UL << 20) | (1UL << 41) | (7UL << 48));
        c.Emit(0x1c00000000000000UL | c.Predicate | 251 | ((ulong)shift << 8) | (0xffffffe0UL << 20));
        c.Emit(0x5c28000000000000UL | c.Predicate | 251 | ((ulong)high << 8) | (251UL << 20));
        c.Emit(0x5c40000000000000UL | c.Predicate | target | (250UL << 8) | (251UL << 20) | (1UL << 41) | (7UL << 48));
    }
}
