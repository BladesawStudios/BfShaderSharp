namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers single-precision fused multiply-add, multiply, add, min/max and rounding.</summary>
/// <remarks>
/// Each takes its sources as a register, immediate, constant-buffer slot or uniform register through the operand
/// builder. FMUL's scale field maps onto Maxwell's multiplier shift, FMNMX with the NaN flag returns the canonical NaN
/// for an unordered pair by comparing first and merging afterwards, and FRND maps the rounding mode onto Maxwell's.
/// Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class FloatArithmeticLowering
{
    private static readonly uint[] ScaleMap = [0, 3, 2, 1, 0, 6, 5, 4];

    public static void Register(OpcodeTable table)
    {
        table.Add(FusedMultiplyAdd, 0x223, 0x423, 0x623, 0x823, 0xa23, 0xc23, 0xe23);
        table.Add(Multiply, 0x220, 0x820, 0xa20, 0xc20);
        table.Add(Add, 0x221, 0x421, 0x621, 0x821, 0xa21, 0xe21);
        table.Add(MinMax, 0x209, 0x809, 0xa09, 0xc09);
        table.Add(Round, 0x307, 0xb07);
    }

    private static bool FusedMultiplyAdd(InstructionContext c)
    {
        uint opcode = c.Opcode;
        if (c.Bits(76, 1) != 0 || (c.Bits(81, 11) & ~(opcode == 0xe23 || opcode == 0xc23 ? 0x400U : 0U)) != 0) return c.Fail("native FFMA denormal/source mode");
        uint a = c.Operands.Operand(0), b = c.Operands.Operand(1), third = c.Operands.Operand(2);
        c.Emit(c.Base(0x5980000000000000UL) | ((ulong)a << 8) | ((ulong)b << 20) | ((ulong)third << 39) | ((ulong)c.Bits(77, 1) << 50) |
               ((ulong)c.Bits(78, 2) << 51) | ((ulong)c.Bits(80, 1) << 53));
        return true;
    }

    private static bool Multiply(InstructionContext c)
    {
        uint scale = c.Bits(84, 3);
        if (c.Bits(76, 1) != 0 || scale == 0 || c.Bits(81, 3) != 0 || c.Bits(87, 4) != 0 || c.Bits(91, 1) != (c.Opcode == 0xc20 ? 1U : 0U) ||
            (scale != 4 && (c.Bits(80, 1) == 0 || c.Bits(78, 2) != 0)))
            return c.Fail("native FMUL scale/mode");
        uint a = c.Operands.Operand(0), b = c.Operands.Operand(1);
        c.Emit(c.Base(0x5c68000000000000UL) | ((ulong)a << 8) | ((ulong)b << 20) | ((ulong)c.Bits(78, 2) << 39) | ((ulong)ScaleMap[scale] << 41) |
               ((ulong)c.Bits(80, 1) << 44) | ((ulong)c.Bits(77, 1) << 50));
        return true;
    }

    private static bool Add(InstructionContext c)
    {
        if (c.Bits(81, 10) != 0 || c.Bits(91, 1) != (c.Opcode == 0xe21 ? 1U : 0U)) return c.Fail("native FADD mode");
        uint form = c.Bits(9, 3);
        uint a = c.Operands.Operand(0), b = c.Operands.Operand(form is 2 or 3 or 7 ? 2u : 1u);
        c.Emit(c.Base(0x5c58000000000000UL) | ((ulong)a << 8) | ((ulong)b << 20) | ((ulong)c.Bits(78, 2) << 39) | ((ulong)c.Bits(80, 1) << 44) | ((ulong)c.Bits(77, 1) << 50));
        return true;
    }

    private static bool MinMax(InstructionContext c)
    {
        if (c.Bits(74, 6) != 0 || c.Bits(82, 5) != 0 || (c.Opcode != 0xc09 && c.Bits(91, 1) != 0)) return c.Fail("native floating min/max mode");
        uint a = c.Operands.Operand(0), b = c.Operands.Operand(1);
        // .NAN (bit 81): an unordered pair gives the canonical NaN. R252 is all ones when both are numbers.
        bool propagateNan = c.Bits(81, 1) != 0;
        if (propagateNan) c.Emit(0x5800000000000000UL | c.Predicate | 252 | ((ulong)a << 8) | ((ulong)b << 20) | (7UL << 39) | (7UL << 48));
        c.Emit(c.Base(0x5c60000000000000UL) | ((ulong)a << 8) | ((ulong)b << 20) | ((ulong)c.Bits(87, 3) << 39) | ((ulong)c.Bits(90, 1) << 42) | ((ulong)c.Bits(80, 1) << 44));
        if (!propagateNan) return true;
        c.Emit(c.Base(0x5c40000000000000UL) | (c.Dest << 8) | (252UL << 20) | (7UL << 48));
        c.Emit(0x0400000000000000UL | c.Predicate | 252 | (252UL << 8) | (0xffffffffUL << 20) | (2UL << 53));
        c.Emit(0x0400000000000000UL | c.Predicate | 252 | (252UL << 8) | (0x7fffffffUL << 20));
        c.Emit(c.Base(0x5c40000000000000UL) | (c.Dest << 8) | (252UL << 20) | (1UL << 41) | (7UL << 48));
        return true;
    }

    // FRND.F32 (0xb07: constant buffer), or the half-precision source forms.
    private static bool Round(InstructionContext c)
    {
        uint opcode = c.Opcode, word = c.Insn.W2;
        if (opcode == 0x307 && (word == 0x00100800 || word == 0x0010c800))
        {
            if (c.Bits(24, 8) != 0 || (c.Insn.W1 & ~0x100000ffU) != 0 || (c.Insn.W3 & 0xc00001ffU) != 0) return c.Fail("native half round mode");
            c.Emit(c.Base(0x5ca8000000000000UL) | (1UL << 8) | (1UL << 10) | ((ulong)c.Bits(32, 8) << 20) | ((ulong)(8 + c.Bits(78, 2)) << 39) | ((ulong)c.Bits(60, 1) << 41));
            return true;
        }

        if (c.Bits(75, 2) != 2 || c.Bits(84, 2) != 2 || c.Bits(81, 3) != 0 || c.Bits(86, 6) != 0) return c.Fail("native floating round format");
        if (opcode == 0xb07 && !c.ValidCbuf()) return c.Fail("native constant buffer address");
        ulong source = opcode == 0xb07 ? c.Cbuf() : (ulong)c.Bits(32, 8) << 20;
        c.Emit(c.Base(opcode == 0xb07 ? 0x4ca8000000000000UL : 0x5ca8000000000000UL) | (2UL << 8) | (2UL << 10) | source | ((ulong)(8 + c.Bits(78, 2)) << 39) |
               ((ulong)c.Bits(80, 1) << 44) | ((ulong)c.Bits(63, 1) << 45) | ((ulong)c.Bits(62, 1) << 49));
        return true;
    }
}
