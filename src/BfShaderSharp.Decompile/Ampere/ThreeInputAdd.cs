namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the three-input add (IADD3) with a third source, negated sources or carries.</summary>
/// <remarks>
/// The sum builds up in R253 with its carry count in R254. The two output predicates (bits 81 and 84) are set when the
/// count reaches one and two, and the extended form adds both carry-in predicates (bits 87 and 77). A negated source
/// is its complement, plus one unless the add is extended. An output that is also the guard predicate is written
/// last, so the guard stays valid for the other. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class ThreeInputAdd
{
    public static bool Applies(InstructionContext c)
    {
        bool extended = c.Bits(74, 1) != 0;
        bool negateA = c.Bits(72, 1) != 0, negateB = c.Opcode != 0x810 && c.Bits(63, 1) != 0, negateC = c.Bits(75, 1) != 0;
        return (extended || c.Bits(81, 6) != 0x3f) &&
               (c.Bits(64, 8) != 255 || c.Bits(84, 3) != 7 || negateA || negateB || negateC || (extended && c.Bits(77, 4) != 15));
    }

    private static void Invert(InstructionContext c, ref uint source, uint target)
    {
        c.Emit(0x0400000000000000UL | c.Predicate | target | ((ulong)source << 8) | (0xffffffffUL << 20) | (2UL << 53));
        source = target;
    }

    private static void Accumulate(InstructionContext c, uint source)
    {
        c.Emit(0x5c10000000000000UL | c.Predicate | 253 | (253UL << 8) | ((ulong)source << 20) | (1UL << 47));
        c.Emit(0x5c10000000000000UL | c.Predicate | 254 | (254UL << 8) | (255UL << 20) | (1UL << 43));
    }

    public static bool Lower(InstructionContext c)
    {
        uint opcode = c.Opcode, firstOutput = c.Bits(81, 3), secondOutput = c.Bits(84, 3);
        bool extended = c.Bits(74, 1) != 0;
        bool negateA = c.Bits(72, 1) != 0, negateB = opcode != 0x810 && c.Bits(63, 1) != 0, negateC = c.Bits(75, 1) != 0;
        if (c.RewrittenUniform || c.GuardedUniform || c.Bits(73, 1) != 0 || c.Bits(76, 1) != 0 || (firstOutput != 7 && firstOutput == secondOutput) ||
            (!extended && (c.Bits(77, 4) != 15 || c.Bits(87, 4) != 15)) || (opcode == 0x210 && (c.Insn.W1 & ~0x800000ffU) != 0) ||
            (opcode == 0xa10 && c.Bits(62, 1) != 0) || (opcode == 0xc10 && (c.Insn.W1 & ~0x8000003fU) != 0))
            return c.Fail("native three-input carry mode");
        uint a = c.Bits(24, 8), b = c.Operands.Operand(1, false), third = c.Bits(64, 8);
        if (negateA) Invert(c, ref a, 250);
        if (negateB) Invert(c, ref b, 251);
        if (negateC) Invert(c, ref third, 252);
        c.Emit(0x5c10000000000000UL | c.Predicate | 253 | ((ulong)a << 8) | ((ulong)b << 20) | (1UL << 47));
        c.Emit(0x5c10000000000000UL | c.Predicate | 254 | (255UL << 8) | (255UL << 20) | (1UL << 43));
        Accumulate(c, third);
        if (extended) AddCarries(c);
        else if ((negateA ? 1U : 0U) + (negateB ? 1U : 0U) + (negateC ? 1U : 0U) is uint ones and not 0)
        {
            c.MovImm(250, ones);
            Accumulate(c, 250);
        }

        if (c.Dest != 255) c.Emit(c.Base(0x5c98000000000000UL) | (253UL << 20) | (15UL << 39));
        SetOutputs(c, firstOutput, secondOutput);
        return true;
    }

    private static void AddCarries(InstructionContext c)
    {
        foreach (uint carry in new[] { c.Bits(87, 4), c.Bits(77, 4) })
        {
            if (carry == 15) continue;
            c.MovImm(250, 1);
            if (carry != 7) c.Emit(0x5ca0000000000000UL | c.Predicate | 250 | (250UL << 8) | (255UL << 20) | ((ulong)(carry & 7) << 39) | ((ulong)(carry >> 3) << 42));
            Accumulate(c, 250);
        }
    }

    // An output that is also the guard predicate is written last.
    private static void SetOutputs(InstructionContext c, uint firstOutput, uint secondOutput)
    {
        bool guardFirst = (c.NativePredicate & 7) != 7 && firstOutput == (c.NativePredicate & 7);
        foreach (uint output in new[] { guardFirst ? 1U : 0U, guardFirst ? 0U : 1U })
        {
            uint target = output != 0 ? secondOutput : firstOutput;
            if (target == 7) continue;
            c.MovImm(250, output + 1);
            c.Emit(0x5b60000000000000UL | c.Predicate | 7 | ((ulong)target << 3) | (254UL << 8) | (250UL << 20) | (7UL << 39) | (6UL << 49));
        }
    }
}
