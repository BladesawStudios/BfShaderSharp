namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers load effective address (LEA): the scaled add, its carry-out form and the high-word form.</summary>
/// <remarks>
/// The low form is a scaled add (ISCADD) with an optional negated second source; with a carry out, the low half of a
/// 64-bit address, the carry is the unsigned overflow of the sum, computed in scratch so that a carry predicate that
/// is also the guard stays correct. The high form (LEA.HI) adds the high word of the shifted pair, with an optional
/// carry in. The second source is a register, immediate, constant-buffer slot or uniform register by opcode. Ported
/// from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class LeaLowering
{
    public static void Register(OpcodeTable table) => table.Add(Lea, 0x211, 0x811, 0xa11, 0xc11);

    private static bool Lea(InstructionContext c) => c.Bits(80, 1) != 0 ? HighWord(c) : LowWord(c);

    private static uint SecondSource(InstructionContext c)
    {
        uint b = c.Opcode == 0xc11 ? c.UniformRegister(c.Bits(32, 8)) : c.Bits(32, 8);
        return c.Opcode == 0xa11 ? c.Operands.Operand(1, false) : b;
    }

    // LEA.HI Rd, Ra, Rb, Rc, s: Rb + the high word of (Rc:Ra << s). Bit 73 sign-extends Ra (Rc is RZ); .X adds the carry predicate in bits 87-90.
    private static bool HighWord(InstructionContext c)
    {
        uint opcode = c.Opcode, high = c.Bits(64, 8);
        bool extended = c.Bits(74, 1) != 0;
        if (c.Bits(72, 1) != 0 || c.Bits(81, 3) != 7 || (high != 255 && c.Bits(73, 1) != 0) || ((opcode == 0x211 || opcode == 0xa11) && c.Bits(62, 2) != 0) ||
            (opcode == 0xc11 && (c.Insn.W1 & ~0x3fU) != 0))
            return c.Fail("native high effective address mode");
        uint b = SecondSource(c);
        if (b == uint.MaxValue) return c.Fail("native uniform LEA register");
        if (opcode == 0x811)
        {
            b = c.Insn.W1 != 0 ? 251U : 255U;
            if (b == 251) c.MovImm(251, c.Insn.W1);
        }

        if (b == 255 && !extended)
        {
            ShiftHigh(c, c.Base(0), high);
            return true;
        }

        ShiftHigh(c, c.Predicate | 250, high);
        if (extended)
        {
            c.MovImm(252, 1);
            c.Emit(0x5ca0000000000000UL | c.Predicate | 252 | (252UL << 8) | (255UL << 20) | ((ulong)c.Bits(87, 3) << 39) | ((ulong)c.Bits(90, 1) << 42));
            c.Emit(0x5c10000000000000UL | c.Predicate | 250 | (250UL << 8) | (252UL << 20));
        }

        c.Emit(c.Base(0x5c10000000000000UL) | (250UL << 8) | ((ulong)b << 20));
        return true;
    }

    // The high word is a right shift of Ra, or with Rc a left funnel shift.
    private static void ShiftHigh(InstructionContext c, ulong target, uint high)
    {
        ulong a = (ulong)c.Bits(24, 8) << 8;
        if (high == 255) c.Emit(target | 0x3828000000000000UL | a | ((ulong)(32 - c.Bits(75, 5)) << 20) | ((ulong)c.Bits(73, 1) << 48));
        else c.Emit(target | 0x36f8000000000000UL | a | ((ulong)high << 39) | ((ulong)c.Bits(75, 5) << 20));
    }

    private static bool LowWord(InstructionContext c)
    {
        uint opcode = c.Opcode, carryOut = c.Bits(81, 3);
        if (c.Bits(72, 1) != 0 || c.Bits(74, 1) != 0 || (carryOut != 7 && ((opcode != 0x811 && c.Bits(63, 1) != 0) || (c.RewrittenUniform && (opcode != 0x211 || carryOut > 1)))) ||
            ((opcode == 0x211 || opcode == 0xa11) && c.Bits(62, 1) != 0) || (opcode == 0xc11 && (c.Insn.W1 & ~0x8000003fU) != 0))
            return c.Fail("native LEA carry/high mode");
        // 0x211 bit 63 negates Rb (LEA Rd, Ra, -Rb, s), as Maxwell ISCADD's bit 48.
        uint b = SecondSource(c);
        if (b == uint.MaxValue) return c.Fail("native uniform LEA register");
        if (opcode == 0x811)
        {
            c.MovImm(251, c.Insn.W1);
            b = 251;
        }

        ulong a = (ulong)c.Bits(24, 8) << 8, scale = (ulong)c.Bits(75, 5) << 39;
        if (opcode == 0xa11 && carryOut != 7)
        {
            // The cbuf is snapshotted in R251. Write the result before the carry predicate, which can also be the instruction's guard.
            c.Emit(c.Base(0x5c18000000000000UL) | a | ((ulong)b << 20) | scale);
            c.Emit(0x5b60000000000000UL | c.Predicate | 7 | ((ulong)carryOut << 3) | (c.Dest << 8) | ((ulong)b << 20) | (7UL << 39) | (1UL << 49));
            return true;
        }

        if (carryOut != 7)
        {
            CarryOut(c, carryOut, b, a, scale);
            return true;
        }

        c.Emit(c.Base(0x5c18000000000000UL) | a | ((ulong)b << 20) | scale | ((ulong)(opcode != 0x811 ? c.Bits(63, 1) : 0U) << 48));
        return true;
    }

    // The sum in R250, carry = sum < Rb (unsigned), as IADD3's carry out; a uniform predicate goes through P(6-n) into R239.
    private static void CarryOut(InstructionContext c, uint carryOut, uint b, ulong a, ulong scale)
    {
        uint target = c.RewrittenUniform ? 6 - carryOut : carryOut, mask = 1U << (int)target;
        c.Emit(0x5c18000000000000UL | c.Predicate | 250 | a | ((ulong)b << 20) | scale);
        if (c.RewrittenUniform) c.Emit(0x38e8000000070000UL | 253 | (255UL << 8) | ((ulong)mask << 20));
        c.Emit(0x5b60000000000000UL | c.Predicate | 7 | ((ulong)target << 3) | (250UL << 8) | ((ulong)b << 20) | (7UL << 39) | (1UL << 49));
        if (c.RewrittenUniform)
        {
            c.Emit(0x38e8000000070000UL | 239 | (239UL << 8) | ((ulong)mask << 20));
            c.Emit(0x38f0000000070000UL | (253UL << 8) | ((ulong)mask << 20));
        }

        c.Emit(c.Base(0x5c98000000000000UL) | (15UL << 39) | (250UL << 20));
    }
}
