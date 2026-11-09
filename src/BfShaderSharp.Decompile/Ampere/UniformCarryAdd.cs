namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the uniform-datapath three-input add (UIADD3) when it takes or produces carries.</summary>
/// <remarks>
/// A uniform predicate lives in a bit of R239, so each carry is moved through a scratch predicate: saved, computed
/// with ordinary instructions, merged back and restored. The extended form adds the carry-in predicates to the sum;
/// the carry-out form builds the sum with its carries in a register pair and then sets each output predicate from the
/// carry count. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class UniformCarryAdd
{
    private static readonly int[] CarryIn = [77, 87];

    // The extended add: sum plus the carry-in predicates at bits 77 and 87.
    public static bool Extended(InstructionContext c)
    {
        if (c.GuardedUniform || (c.Insn.W2 & ~0x0781e0ffU) != 0x007e0400U || (c.Insn.W1 & ~0xffU) != 0) return c.Fail("native uniform extended add mode");
        c.Emit(0x5c10000000000000UL | c.Predicate | 250 | ((ulong)c.Bits(24, 8) << 8) | ((ulong)c.Bits(32, 8) << 20));
        c.Emit(0x5c10000000000000UL | c.Predicate | 250 | (250UL << 8) | ((ulong)c.Bits(64, 8) << 20));
        uint mask = 0;
        foreach (int bit in CarryIn)
        {
            uint source = c.Bits(bit, 3);
            if (source != 7) mask |= 1U << (int)(6 - source);
        }

        c.SavePredicate(mask);
        c.Emit(0x38f0000000070000UL | (239UL << 8) | ((ulong)mask << 20));
        c.MovImm(252, 1);
        foreach (int bit in CarryIn)
        {
            uint source = c.Bits(bit, 3);
            c.Emit(0x5ca0000000000000UL | c.Predicate | 251 | (252UL << 8) | (255UL << 20) | ((ulong)(source == 7 ? 7U : 6 - source) << 39) | ((ulong)c.Bits(bit + 3, 1) << 42));
            c.Emit(0x5c10000000000000UL | c.Predicate | 250 | (250UL << 8) | (251UL << 20));
        }

        c.RestorePredicate(mask);
        c.Emit(c.Base(0x5c98000000000000UL) | (15UL << 39) | (250UL << 20));
        return true;
    }

    // The carry-out add: the sum in R250:R251 with up to two carries, each output predicate set from the carry count.
    public static bool CarryOut(InstructionContext c)
    {
        uint first = c.Bits(81, 3), second = c.Bits(84, 3);
        if (c.GuardedUniform || (c.Insn.W2 & ~0x007e00ffU) != 0x0781e000U || (c.Insn.W1 & ~0xffU) != 0 || (first != 7 && first == second))
            return c.Fail("native uniform add carry mode");
        c.Emit(0x5c10000000000000UL | c.Predicate | 250 | ((ulong)c.Bits(24, 8) << 8) | ((ulong)c.Bits(32, 8) << 20) | (1UL << 47));
        c.Emit(0x5c10000000000000UL | c.Predicate | 251 | (255UL << 8) | (255UL << 20) | (1UL << 43));
        c.Emit(0x5c10000000000000UL | c.Predicate | 250 | (250UL << 8) | ((ulong)c.Bits(64, 8) << 20) | (1UL << 47));
        c.Emit(0x5c10000000000000UL | c.Predicate | 251 | (251UL << 8) | (255UL << 20) | (1UL << 43));
        uint mask = (first == 7 ? 0U : 1U << (int)(6 - first)) | (second == 7 ? 0U : 1U << (int)(6 - second));
        c.SavePredicate(mask);
        c.Emit(c.Base(0x5c98000000000000UL) | (15UL << 39) | (250UL << 20));
        for (uint output = 0; output < 2; output++)
        {
            uint target = output != 0 ? second : first;
            if (target == 7) continue;
            c.MovImm(252, output + 1);
            c.Emit(0x5b60000000000000UL | c.Predicate | 7 | ((ulong)(6 - target) << 3) | (251UL << 8) | (252UL << 20) | (7UL << 39) | (6UL << 49));
        }

        c.Emit(0x38e8000000070000UL | 239 | (239UL << 8) | ((ulong)mask << 20));
        c.RestorePredicate(mask);
        return true;
    }
}
