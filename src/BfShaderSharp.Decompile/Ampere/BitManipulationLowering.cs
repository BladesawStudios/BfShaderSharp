namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers bit reversal, leading-bit and population-count instructions, and the 32-bit shift family.</summary>
/// <remarks>
/// BREV is a bit-reversing zero shift. A shift whose amount comes from a register, a constant buffer or a uniform
/// register is accepted in the forms that produce a plain 32-bit result with a zero word outside the selected half; a
/// shift of a register pair is a funnel built by <see cref="IntegerMath.RightShiftU64Low"/>. Ported from the Eden
/// emulator (GPL-3.0-or-later).
/// </remarks>
internal static class BitManipulationLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(BitReverse, 0x301);
        table.Add(Shift, 0x219, 0xa19, 0xc19);
        table.Add(LeadingBit, 0x300, 0xd00);
        table.Add(PopulationCount, 0x309, 0xd09);
    }

    private static bool BitReverse(InstructionContext c)
    {
        if (c.Insn.W2 != 0 || c.Insn.W1 > 255) return c.Fail("native bit reverse mode");
        c.Emit(c.Base(0x3828000000000000UL) | ((ulong)c.Insn.W1 << 8) | (1UL << 39) | (1UL << 40));
        return true;
    }

    // U32 register (or cbuf, 0xa19, or uniform, 0xc19) shifts with a zero word outside the selected result.
    private static bool Shift(InstructionContext c)
    {
        uint opcode = c.Opcode, low = c.Bits(24, 8), high = c.Bits(64, 8);
        bool constant = opcode == 0xa19;
        // A constant shift amount is loaded into R251 once the form is known.
        uint shift = opcode == 0xc19 ? c.UniformRegister(c.Bits(32, 8)) : constant ? 251U : c.Bits(32, 8);
        bool shiftOperand = constant ? c.ValidCbuf() && c.Bits(62, 2) == 0 : c.Insn.W1 <= 255;
        bool right = c.Bits(76, 1) != 0, highResult = c.Bits(80, 1) != 0;
        if (opcode == 0x219 && shiftOperand && (c.Insn.W2 & ~0xffU) == 0x00001200U)
        {
            c.Integer.RightShiftU64Low((uint)c.Dest, low, high, shift);
            return true;
        }

        if (opcode == 0x219 && low == 255 && shiftOperand && (c.Insn.W2 & ~0x8ffU) == 0x00011400U && (c.Insn.W3 & 0xc00001ffU) == 0)
        {
            bool wrap = c.Bits(75, 1) != 0;
            if (!wrap) c.Emit(0x3820000000000000UL | c.Predicate | 250 | ((ulong)shift << 8) | (31UL << 20) | (7UL << 39));
            c.Emit(c.Base(0x5c28000000000000UL) | ((ulong)high << 8) | ((ulong)(wrap ? shift : 250U) << 20) | (1UL << 39) | (1UL << 48));
            return true;
        }

        uint ignored = 0x000118ffU | (opcode == 0xc19 ? 0x08000000U : 0U);
        // SHF.R.U32 of the low word with a zero high word: a plain logical right shift.
        if (right && !highResult && high == 255 && c.Bits(73, 2) == 3 && shift != uint.MaxValue && shiftOperand && (c.Insn.W2 & ~ignored) == 0x00000600U)
        {
            LoadShift(c, constant);
            c.Emit(c.Base(0x5c28000000000000UL) | ((ulong)low << 8) | ((ulong)shift << 20));
            return true;
        }

        if (!shiftOperand || c.Bits(73, 2) != 3 || shift == uint.MaxValue || (c.Insn.W2 & ~ignored) != 0x00000600U ||
            (right ? !highResult || low != 255 : highResult ? low != 255 : high != 255))
            return c.Fail("native register shift mode");
        LoadShift(c, constant);
        c.Emit(c.Base(right ? 0x5cf8000000000000UL : 0x5bf8000000000000UL) | ((ulong)(right ? high : 255U) << 8) | ((ulong)shift << 20) |
               ((ulong)(right ? 255U : highResult ? high : low) << 39) | ((ulong)c.Bits(75, 1) << 50));
        return true;
    }

    private static void LoadShift(InstructionContext c, bool constant)
    {
        if (constant) c.Operands.Materialize(2, 0, 251, false, false);
    }

    // FLO, including a uniform source.
    private static bool LeadingBit(InstructionContext c)
    {
        if ((c.Insn.W2 & ~(0x600U | (c.Opcode == 0xd00 ? 0x08000000U : 0U))) != 0x000e0000U) return c.Fail("native leading bit mode");
        uint source = c.Operands.Operand(1, false);
        c.Emit(c.Base(0x5c30000000000000UL) | ((ulong)source << 20) | ((ulong)c.Bits(63, 1) << 40) | ((ulong)c.Bits(74, 1) << 41) | ((ulong)c.Bits(73, 1) << 48));
        return true;
    }

    // POPC, including a uniform source.
    private static bool PopulationCount(InstructionContext c)
    {
        if (c.Insn.W2 != (c.Opcode == 0xd09 ? 0x08000000U : 0U)) return c.Fail("native population count mode");
        uint source = c.Operands.Operand(1, false);
        c.Emit(c.Base(0x5c08000000000000UL) | ((ulong)source << 20) | ((ulong)c.Bits(63, 1) << 40));
        return true;
    }
}
