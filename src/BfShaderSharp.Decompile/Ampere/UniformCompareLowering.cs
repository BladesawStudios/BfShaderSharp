namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the uniform integer compare (UISETP) and uniform select (USEL).</summary>
/// <remarks>
/// A uniform predicate lives in a bit of R239, so each operation loads the predicates it reads into scratch predicates,
/// computes with the ordinary instruction, and merges the result back. A clamp written as UISETP followed by USEL with
/// the same bound is kept as one unsigned minimum so the indirect-table analysis downstream can recognize it; the
/// compare may sit a few instructions earlier when nothing in between touches uniform state, branches, or is branched
/// to. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class UniformCompareLowering
{
    private const int LookBack = 8;

    public static void Register(OpcodeTable table)
    {
        table.Add(Compare, 0x28c, 0x88c);
        table.Add(Select, 0x287, 0x887);
    }

    private static bool Compare(InstructionContext c)
    {
        uint output = c.Bits(81, 3);
        uint a = c.UniformRegister(c.Bits(24, 8)), b = c.Opcode == 0x88c ? 252U : c.UniformRegister(c.Bits(32, 8));
        uint combine = c.Bits(87, 3), combineOp = c.Bits(74, 2);
        if (output > 6 || c.Bits(84, 3) != 7 || c.Bits(72, 1) != 0 || combineOp == 3 || a == uint.MaxValue || b == uint.MaxValue) return c.Fail("native uniform comparison mode");
        uint temporary = 6 - output, combineSlot = combine == 7 ? 7U : 6 - combine;
        uint combineMask = combine == 7 ? 0U : 1U << (int)combineSlot;
        bool guarded = c.NativePredicate != 7;
        uint temporaryMask = 1U << (int)temporary;
        uint loadMask = combineMask | (guarded ? temporaryMask | (1U << (int)c.GuardSlot) : 0U);
        uint mask = temporaryMask | loadMask;
        if (c.Opcode == 0x88c) c.Emit(0x0100000000000000UL | (7UL << 16) | 252 | (15UL << 12) | ((ulong)c.Insn.W1 << 20));
        c.Emit(0x38e8000000070000UL | 253 | (255UL << 8) | ((ulong)mask << 20));
        if (loadMask != 0) c.Emit(0x38f0000000070000UL | (239UL << 8) | ((ulong)loadMask << 20));
        c.Emit(0x5b60000000000000UL | ((ulong)(guarded ? (c.NativePredicate & 8) | c.GuardSlot : 7U) << 16) | 7 | ((ulong)temporary << 3) | ((ulong)a << 8) |
               ((ulong)b << 20) | ((ulong)combineSlot << 39) | ((ulong)c.Bits(90, 1) << 42) | ((ulong)combineOp << 45) | ((ulong)c.Bits(73, 1) << 48) |
               ((ulong)c.Bits(76, 3) << 49));
        c.Emit(0x38e8000000070000UL | 239 | (239UL << 8) | ((ulong)temporaryMask << 20));
        c.Emit(0x38f0000000070000UL | (253UL << 8) | ((ulong)mask << 20));
        return true;
    }

    // USEL, UP0/UP1; ordinary predicates are preserved.
    private static bool Select(InstructionContext c)
    {
        uint condition = c.Bits(87, 3), target = c.UniformRegister((uint)c.Dest);
        uint a = c.UniformRegister(c.Bits(24, 8)), b = c.Opcode == 0x887 ? 252U : c.UniformRegister(c.Bits(32, 8));
        if (condition > 6 || target == uint.MaxValue || a == uint.MaxValue || b == uint.MaxValue || (c.Insn.W2 & ~0x07800000U) != 0x08000000U)
            return c.Fail("native uniform select mode");
        uint compare = FindCompare(c);
        bool targetBetween = c.State.Relocations.Any(r => r.Target > compare && r.Target <= c.Index);
        AmpereInstruction earlier = c.State.Code[compare];
        if (c.Opcode == 0x887 && c.NativePredicate == 7 && compare < c.Index && !targetBetween && condition == 0 && c.Insn.W1 <= 0x7ffff &&
            earlier.Bits(12, 4) == 7 && earlier.Bits(24, 8) == c.Bits(24, 8) && earlier.W1 == c.Insn.W1 && earlier.W2 == 0x0bf01070 && c.Insn.W2 == 0x08000000)
        {
            // Preserve the unsigned clamp idiom for indirect-table analysis.
            c.Emit(0x3820000000070000UL | target | ((ulong)a << 8) | ((ulong)c.Insn.W1 << 20) | (7UL << 39));
            return true;
        }

        if (c.Opcode == 0x887) c.Emit(0x0100000000000000UL | (7UL << 16) | 252 | (15UL << 12) | ((ulong)c.Insn.W1 << 20));
        uint temporary = 6 - condition;
        uint mask = (1U << (int)temporary) | (c.NativePredicate != 7 ? 1U << (int)c.GuardSlot : 0U);
        c.Emit(0x38e8000000070000UL | 253 | (255UL << 8) | ((ulong)mask << 20));
        c.Emit(0x38f0000000070000UL | (239UL << 8) | ((ulong)mask << 20));
        c.Emit(0x5ca0000000000000UL | ((ulong)((c.NativePredicate & 8) | (c.NativePredicate != 7 ? c.GuardSlot : 7U)) << 16) | target | ((ulong)a << 8) |
               ((ulong)b << 20) | ((ulong)temporary << 39) | ((ulong)c.Bits(90, 1) << 42));
        c.Emit(0x38f0000000070000UL | (253UL << 8) | ((ulong)mask << 20));
        return true;
    }

    // The clamp's UISETP may sit a few instructions back, with only instructions between that write no uniform state and do not branch.
    private static uint FindCompare(InstructionContext c)
    {
        for (uint back = 1; back <= LookBack && back <= c.Index; back++)
        {
            uint earlier = c.State.Code[c.Index - back].Opcode, datapath = earlier & 0x1ff;
            if (earlier == 0x88c) return c.Index - back;
            if ((datapath >= 0x080 && datapath <= 0x0bf) || (datapath >= 0x1c2 && datapath <= 0x1c4) || (earlier >= 0x940 && earlier <= 0x95f)) break;
        }

        return c.Index;
    }
}
