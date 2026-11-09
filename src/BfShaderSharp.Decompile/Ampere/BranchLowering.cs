namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers branches: the relative branch (BRA), the computed branch (BRX, BRXU) and the reconvergence instructions.</summary>
/// <remarks>
/// A branch is emitted with its distance left open and recorded for patching once every instruction has a place. The
/// two low bits of a branch offset select a warp mode: threads here run converged, so a divergence-only branch is
/// dropped and a convergence-only branch always branches. Reconvergence instructions (BSSY, BSYNC, BREAK, WARPSYNC)
/// only manage warp reconvergence and are dropped. A computed branch jumps through a table of native program counters
/// and ends the program at its closing self-branch. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class BranchLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(Computed, 0x949, 0x958);
        table.Add(Reconverge, 0x945, 0x941, 0x942, 0x948);
        table.Add(Branch, 0x947);
    }

    // The signed 46-bit offset of a branch, from bits 32-63 and 64-81.
    public static long Offset(InstructionContext c, uint lowMask = 0)
    {
        ulong raw = (c.Insn.W1 & ~lowMask) | ((ulong)(c.Insn.W2 & 0x3ffff) << 32);
        return (long)(raw << 14) >> 14;
    }

    private static bool Reconverge(InstructionContext c) => true;

    private static bool Computed(InstructionContext c)
    {
        bool uniform = c.Opcode == 0x958;
        if (c.Bits(12, 4) != 7 || (c.Insn.W2 & ~0x3ffffU) != (uniform ? 0x0b800000U : 0x03800000U)) return c.Fail("native computed branch mode");
        if ((long)c.Index * 16 + 16 + Offset(c) != 0) return c.Fail("native computed branch relative base");
        long offset = -(long)LoweringState.AddressOf((uint)c.Output.Count) - 8;
        if (offset < -0x800000) return c.Fail("native computed branch range");
        uint source = uniform ? c.UniformRegister(c.Bits(24, 8)) : c.Bits(24, 8);
        if (source == uint.MaxValue) return c.Fail("native uniform computed branch source");
        c.Emit(0xe25000000000000fUL | c.Predicate | ((ulong)source << 8) | (((ulong)offset & 0xffffff) << 20));
        c.State.ComputedBranch = true;
        return true;
    }

    // BRA. .DIV (2) branches when the warp has diverged and .CONV (3) when its active threads are the mask in a uniform register; threads run converged here.
    private static bool Branch(InstructionContext c)
    {
        uint mode = c.Bits(32, 2);
        if ((mode <= 1 && c.Bits(91, 1) != 0) || (mode == 1 && (c.Bits(82, 5) != 0 || c.Bits(92, 13) != 0))) return c.Fail("native branch convergence mode");
        if (mode == 2) return true;
        ulong branchPredicate = c.Predicate;
        if (c.Bits(87, 4) != 7)
        {
            if (c.Bits(12, 4) != 7)
            {
                c.State.Relocations.Add(new Relocation((uint)c.Output.Count, c.Index + 1));
                c.Emit(0xe24000000000000fUL | ((ulong)(c.Bits(12, 4) ^ 8U) << 16));
            }

            branchPredicate = (ulong)c.Bits(87, 4) << 16;
        }

        long target = (long)c.Index * 16 + 16 + Offset(c, 3);
        if (target < 0 || target % 16 != 0) return c.Fail("native branch destination alignment");
        c.State.Relocations.Add(new Relocation((uint)c.Output.Count, target / 16));
        c.Emit(0xe24000000000000fUL | branchPredicate);
        // The closing self-branch of a jump table ends the program.
        if (c.State.ComputedBranch && target == (long)c.Index * 16 && branchPredicate == 7UL << 16) return c.State.Finish(c.Index);
        return true;
    }
}
