namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the funnel shifts (SHF, USHF) and sign extension (SGXT).</summary>
/// <remarks>
/// A funnel shift with an immediate amount of at most 32 maps onto Maxwell's shift or funnel shift, depending on
/// whether the high word is zero; the 64-bit right shift needs no clamp in that range. Shifts by a uniform amount
/// operate on one register of the pair, the other being zero. A shift of an immediate by a register amount puts the
/// immediate in scratch first. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class ShiftLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(UniformShift, 0x299);
        table.Add(UniformImmediateShift, 0x499);
        table.Add(ImmediateLeftShift, 0x419);
        table.Add(Funnel, 0x819);
        table.Add(SignExtend, 0x81a);
    }

    private static bool SignExtend(InstructionContext c)
    {
        if ((c.Insn.W2 & ~0x200U) != 0 || c.Insn.W1 > 32 || c.Dest >= 255) return c.Fail("native sign extend mode");
        c.Emit(c.Base(0x3800000000000000UL) | ((ulong)c.Bits(24, 8) << 8) | ((ulong)(c.Insn.W1 << 8) << 20) | ((ulong)c.Bits(73, 1) << 48));
        return true;
    }

    // USHF by a uniform amount, one register shifted (SHL/SHR, bit 75 wraps).
    private static bool UniformShift(InstructionContext c)
    {
        uint destination = c.UniformRegister(c.Bits(16, 8)), low = c.UniformRegister(c.Bits(24, 8)), shift = c.UniformRegister(c.Bits(32, 8)), high = c.UniformRegister(c.Bits(64, 8));
        bool right = c.Bits(76, 1) != 0, highResult = c.Bits(80, 1) != 0;
        if (destination == uint.MaxValue || destination == 255 || low == uint.MaxValue || shift == uint.MaxValue || high == uint.MaxValue || c.Bits(73, 2) != 3 ||
            (c.Insn.W2 & ~0x000118ffU) != 0x08000600U)
            return c.Fail("native uniform register shift mode");
        uint source;
        if (highResult && low == 255) source = high;
        else if (!highResult && high == 255) source = low;
        else return c.Fail("native uniform funnel shift");
        c.Emit((right ? 0x5c28000000000000UL : 0x5c48000000000000UL) | c.Predicate | destination | ((ulong)source << 8) | ((ulong)shift << 20) | ((ulong)c.Bits(75, 1) << 39));
        return true;
    }

    private static bool UniformImmediateShift(InstructionContext c)
    {
        uint target = c.UniformRegister((uint)c.Dest), shift = c.UniformRegister(c.Bits(64, 8));
        if (c.Bits(24, 8) != 63 || (c.Insn.W2 & ~0x8ffU) != 0x08010600 || (c.Insn.W3 & 0xc00001ffU) != 0 || target == uint.MaxValue || target == 255 || shift == uint.MaxValue)
            return c.Fail("native uniform immediate high-word shift mode");
        c.MovImm(250, c.Insn.W1);
        c.Emit(0x5c48000000000000UL | c.Predicate | target | (250UL << 8) | ((ulong)shift << 20) | ((ulong)c.Bits(75, 1) << 39));
        return true;
    }

    // SHF.L.U32.HI Rd, RZ, Rb, imm: W uses Rb & 31; otherwise min(Rb, 32).
    private static bool ImmediateLeftShift(InstructionContext c)
    {
        if (c.Bits(24, 8) != 255 || (c.Insn.W2 & ~0x8ffU) != 0x00010600 || (c.Bits(75, 1) == 0 && (c.Insn.W3 & 0xc00001ffU) != 0))
            return c.Fail("native funnel shift immediate mode");
        c.MovImm(250, c.Insn.W1);
        c.Emit(c.Base(0x5c48000000000000UL) | (250UL << 8) | ((ulong)c.Bits(64, 8) << 20) | ((ulong)c.Bits(75, 1) << 39));
        return true;
    }

    // SHF.HI with an immediate shift.
    private static bool Funnel(InstructionContext c)
    {
        uint type = c.Bits(73, 2), high = c.Bits(64, 8), shift = c.Insn.W1, low = c.Bits(24, 8);
        bool signed = type == 2, wrap = c.Bits(75, 1) != 0, right = c.Bits(76, 1) != 0, wide = c.Bits(80, 1) != 0, small = shift <= 32;
        ulong a = (ulong)low << 8;
        if (!wide && right && type == 1 && !wrap && small)
        {
            // SHF.R.U64 funnel: the translator reads bits 20-38 as the immediate, so the clamp (bit 38) would turn every shift into 63; shifts up to 32 need none.
            c.Emit(c.Base(0x38f8000000000000UL) | a | ((ulong)shift << 20) | ((ulong)high << 39));
            return true;
        }

        if (!wide && !right && high == 255 && type == 3 && !wrap && small)
        {
            c.Emit(c.Base(0x3848000000000000UL) | a | ((ulong)shift << 20));
            return true;
        }

        if (!wide && right && high == 255 && (type == 3 || signed) && !wrap && small)
        {
            // SHF.R low word with a zero high word: a logical right shift.
            c.Emit(c.Base(0x3828000000000000UL) | a | ((ulong)shift << 20));
            return true;
        }

        // U64 left shifts through 32 have the same high word as a 32-bit funnel shift.
        bool leftWide = type == 1 && !right;
        if ((type != 3 && !signed && !leftWide) || wrap || !wide || !small) return c.Fail("native funnel-shift mode");
        if (right)
        {
            if (low != 255) return c.Fail("native nonzero low right-shift word");
            c.Emit(c.Base(0x3828000000000000UL) | ((ulong)high << 8) | ((ulong)shift << 20) | (InstructionContext.Flag(signed) << 48));
            return true;
        }

        if (signed) return c.Fail("native signed left funnel shift");
        c.Emit(c.Base(0x36f8000000000000UL) | a | ((ulong)high << 39) | ((ulong)shift << 20));
        return true;
    }
}
