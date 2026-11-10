namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the three-input integer add (IADD3) in its register, immediate, constant-buffer and uniform forms.</summary>
/// <remarks>
/// The forms are tried from the most specific: the uniform carry forms, the 64-bit add (a carry-out add followed by a
/// carry-in add, with uniform predicates kept in R239), the three-input forms with several carries or negations
/// (<see cref="ThreeInputAdd"/>), the constant-buffer carry forms, and finally the plain add with an optional third
/// source. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class IntegerAddLowering
{
    public static void Register(OpcodeTable table) => table.Add(Add, 0x210, 0x810, 0xa10, 0xc10);

    private static bool Add(InstructionContext c)
    {
        uint opcode = c.Opcode;
        if (c.RewrittenUniform && opcode == 0x210 && c.Bits(74, 1) != 0 && c.Bits(81, 6) == 0x3f) return UniformCarryAdd.Extended(c);
        if (c.RewrittenUniform && opcode == 0x210 && c.Bits(81, 6) != 0x3f) return UniformCarryAdd.CarryOut(c);
        if (IsWideAdd(c)) return WideAdd(c);
        if (ThreeInputAdd.Applies(c)) return ThreeInputAdd.Lower(c);
        if ((opcode == 0xa10 || opcode == 0xc10) && c.Bits(81, 3) != 7) return ConstantCarry(c);
        if ((opcode == 0xa10 || opcode == 0xc10) && c.Bits(74, 1) != 0) return ExtendedConstant(c);
        bool negateB = opcode != 0x810 && c.Bits(63, 1) != 0;
        // Third source, optionally negated (bit 75; 63 and 72 negate b and a).
        if (c.Bits(64, 8) != 255 && (c.Bits(73, 4) & ~4U) == 0 && c.Bits(77, 7) == 0x7f && c.Bits(84, 7) == 0x7f)
        {
            uint b = c.Operands.Operand(1, false);
            c.Emit(0x5c10000000000000UL | c.Predicate | 250 | ((ulong)c.Bits(24, 8) << 8) | ((ulong)b << 20) | (InstructionContext.Flag(negateB) << 48) | ((ulong)c.Bits(72, 1) << 49));
            c.Emit(c.Base(0x5c10000000000000UL) | (250UL << 8) | ((ulong)c.Bits(64, 8) << 20) | ((ulong)c.Bits(75, 1) << 48));
            return true;
        }

        if (c.Bits(64, 8) != 255 || c.Bits(73, 4) != 0 || c.Bits(77, 7) != 0x7f || c.Bits(84, 7) != 0x7f) return c.Fail("native integer carry/third source");
        return Plain(c);
    }

    private static bool Plain(InstructionContext c)
    {
        uint opcode = c.Opcode;
        ulong a = (ulong)c.Bits(24, 8) << 8;
        if (opcode == 0xa10 || opcode == 0xc10)
        {
            if (opcode == 0xc10 && (c.Insn.W1 & ~0x8000003fU) != 0) return c.Fail("native uniform add operand");
            uint b = c.Operands.Operand(1, false);
            c.Emit(c.Base(0x5c10000000000000UL) | a | ((ulong)b << 20) | ((ulong)c.Bits(63, 1) << 48) | ((ulong)c.Bits(72, 1) << 49));
        }
        else if (opcode == 0x210)
        {
            if ((c.Insn.W1 & ~0x800000ffU) != 0) return c.Fail("native integer add register modifiers");
            c.Emit(c.Base(0x5c10000000000000UL) | a | ((ulong)c.Bits(32, 8) << 20) | ((ulong)c.Bits(63, 1) << 48) | ((ulong)c.Bits(72, 1) << 49));
        }
        else
        {
            c.Emit(c.Base(0x1c00000000000000UL) | a | ((ulong)c.Insn.W1 << 20) | ((ulong)c.Bits(72, 1) << 56));
        }

        return true;
    }

    // 64-bit adds: IADD3 with a carry-out predicate (bits 81-83), then IADD3.X (bit 74) with a carry-in predicate (bits 87-89, negate 90).
    private static bool IsWideAdd(InstructionContext c) =>
        (c.Opcode == 0x210 || c.Opcode == 0x810) && c.Bits(64, 8) == 255 && c.Bits(73, 1) == 0 && c.Bits(75, 2) == 0 && c.Bits(84, 3) == 7 &&
        c.Bits(77, 4) == 15 && c.Bits(72, 1) == 0 && (c.Bits(81, 3) != 7 || c.Bits(74, 1) != 0) && (c.Opcode == 0x810 || c.Bits(63, 1) == 0);

    private static bool WideAdd(InstructionContext c)
    {
        bool carryIn = c.Bits(74, 1) != 0;
        uint carryOut = c.Bits(81, 3), a = c.Bits(24, 8);
        bool uniform = c.RewrittenUniform;
        if ((carryIn && carryOut != 7) || (c.Opcode == 0x210 && (c.Insn.W1 & ~0xffU) != 0))
            return c.Fail("native integer carry mode");
        if (c.Opcode == 0x810) c.Emit(0x1c00000000000000UL | c.Predicate | 250 | ((ulong)a << 8) | ((ulong)c.Insn.W1 << 20));
        else c.Emit(0x5c10000000000000UL | c.Predicate | 250 | ((ulong)a << 8) | ((ulong)c.Bits(32, 8) << 20));
        if (!carryIn)
        {
            // carry = (a + b) < a, unsigned
            uint target = uniform ? 6 - carryOut : carryOut, mask = 1U << (int)target;
            if (uniform) c.SavePredicate(mask);
            c.Emit(0x5b60000000000000UL | c.Predicate | 7 | ((ulong)target << 3) | (250UL << 8) | ((ulong)a << 20) | (7UL << 39) | (1UL << 49));
            if (uniform)
            {
                c.Emit(0x38e8000000070000UL | 239 | (239UL << 8) | ((ulong)mask << 20));
                c.RestorePredicate(mask);
            }

            c.Emit(c.Base(0x5c98000000000000UL) | (15UL << 39) | (250UL << 20));
            return true;
        }

        uint condition = c.Bits(87, 3), source = uniform && condition != 7 ? 6 - condition : condition, selected = 1U << (int)source;
        c.MovImm(252, 1);
        if (uniform && condition != 7)
        {
            c.SavePredicate(selected);
            c.Emit(0x38f0000000070000UL | (239UL << 8) | ((ulong)selected << 20));
        }

        c.Emit(0x5ca0000000000000UL | c.Predicate | 251 | (252UL << 8) | (255UL << 20) | ((ulong)source << 39) | ((ulong)c.Bits(90, 1) << 42));
        if (uniform && condition != 7) c.RestorePredicate(selected);
        c.Emit(c.Base(0x5c10000000000000UL) | (250UL << 8) | (251UL << 20));
        return true;
    }

    // A constant-buffer or uniform second source with a carry out: Rd = Ra + b, carry = Rd < b.
    private static bool ConstantCarry(InstructionContext c)
    {
        if (c.Bits(64, 8) != 255 || c.Bits(73, 4) != 0 || c.Bits(84, 7) != 0x7f || c.Bits(77, 4) != 15 || c.Bits(63, 1) != 0 || c.Bits(72, 1) != 0 ||
            (c.Opcode == 0xc10 && (c.Insn.W1 & ~0x3fU) != 0))
            return c.Fail("native constant add carry mode");
        uint b = c.Operands.Operand(1, false);
        c.Emit(c.Base(0x5c10000000000000UL) | ((ulong)c.Bits(24, 8) << 8) | ((ulong)b << 20));
        c.Emit(0x5b60000000000000UL | c.Predicate | 7 | ((ulong)c.Bits(81, 3) << 3) | (c.Dest << 8) | ((ulong)b << 20) | (7UL << 39) | (1UL << 49));
        return true;
    }

    // IADD3.X Ra + constant buffer or uniform register + carry predicate.
    private static bool ExtendedConstant(InstructionContext c)
    {
        if ((c.Insn.W2 & ~(0x07800000U | (c.Opcode == 0xc10 ? 0x08000000U : 0U))) != 0x007fe4ffU || (c.Opcode == 0xc10 && (c.Insn.W1 & ~0x3fU) != 0))
            return c.Fail("native extended constant add mode");
        uint a = c.Bits(24, 8), b = c.Operands.Operand(1, false);
        c.MovImm(250, 1);
        c.Emit(0x5ca0000000000000UL | c.Predicate | 250 | (250UL << 8) | (255UL << 20) | ((ulong)c.Bits(87, 3) << 39) | ((ulong)c.Bits(90, 1) << 42));
        if (a != 255) c.Emit(0x5c10000000000000UL | c.Predicate | 250 | ((ulong)a << 8) | (250UL << 20));
        c.Emit(c.Base(0x5c10000000000000UL) | ((ulong)b << 8) | (250UL << 20));
        return true;
    }
}
