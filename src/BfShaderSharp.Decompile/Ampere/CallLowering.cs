namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers subroutine calls (CALL.REL.NOINC) and returns (RET.REL.NODEC) with their return-address register.</summary>
/// <remarks>
/// A call is accepted when it follows the instruction that loads the return address into a register, or when the
/// control-flow scan has shown the whole program's calls and returns to be well formed. All calls to one function must
/// agree on that register. A return must restore the link: its register is the link, or was last written as a copy of
/// it within the function. The main program must have exited before functions begin. The program is complete at a
/// return once every call target lies behind it. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class CallLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(Call, 0x944);
        table.Add(Return, 0x950);
    }

    private static bool Call(InstructionContext c)
    {
        LoweringState state = c.State;
        AmpereInstruction[] code = state.Code;
        uint index = c.Index;
        // A predicated NOINC forward transfer can end a loop at its reconvergence point without a link setup or matching return.
        if (c.NativePredicate != 7 && c.Insn.W1 == 16 && c.Insn.W2 == 0x03c00000 && index + 2 < code.Length && code[index + 1].Opcode == 0x947)
        {
            state.Relocations.Add(new Relocation((uint)c.Output.Count, index + 2));
            c.Emit(0xe24000000000000fUL | c.Predicate);
            return true;
        }

        bool adjacent = index != 0 && (code[index - 1].W0 & ~0x00ff0000U) == 0x00007802 && code[index - 1].Bits(16, 8) < 250 &&
                        code[index - 1].W1 == (index + 1) * 16 && code[index - 1].W2 == 0xf00;
        if (index == 0 || c.Insn.W0 != 0x7944 || (c.Insn.W2 & ~0x3ffffU) != 0x03c00000 || (!adjacent && !state.Control.BoundedControl))
            return c.Fail("native call linkage mode");
        long target = (long)index * 16 + 16 + BranchLowering.Offset(c);
        if (target < 0 || target % 16 != 0) return c.Fail("native call destination alignment");
        uint link = adjacent ? code[index - 1].Bits(16, 8) : state.Control.BoundedLinks[index];
        for (int call = 0; call < state.CallTargets.Count; call++)
        {
            if (state.CallTargets[call] == target / 16 && state.CallLinks[call] != link) return c.Fail("native inconsistent call linkage");
        }

        state.CallTargets.Add((uint)(target / 16));
        state.CallLinks.Add(link);
        state.Relocations.Add(new Relocation((uint)c.Output.Count, target / 16, true));
        c.Emit(0xe26000000007000fUL);
        return true;
    }

    // The innermost function the instruction is in: its entry and the register holding its return address.
    private static (uint? Link, uint Entry) ActiveFunction(InstructionContext c)
    {
        LoweringState state = c.State;
        uint? link = null, entry = 0;
        if (!state.MainExited || (state.GeometryExit != null && (ulong)c.Index * 16 >= state.GeometryExit.HandlerOffset)) return (link, 0);
        for (int call = 0; call < state.CallTargets.Count; call++)
        {
            if (state.CallTargets[call] <= c.Index && state.CallTargets[call] >= entry)
            {
                entry = state.CallTargets[call];
                link = state.CallLinks[call];
            }
        }

        return (link, entry.Value);
    }

    // Whether an earlier instruction copies the link into the return register (MOV or IMAD.MOV.U32 Rd, RZ, RZ, Rlink).
    private static bool CopiesLink(AmpereInstruction prior, uint returnRegister, uint link)
    {
        if (prior.Bits(16, 8) != returnRegister || prior.Bits(12, 4) != 7) return false;
        if ((prior.W0 & 0xff00ffffU) == 0x00007202U) return prior.W1 == link && prior.W2 == 0xf00;
        if ((prior.W0 & 0xff00ffffU) == 0xff007224U) return prior.W1 == 0xff && prior.W2 == (0x078e0000U | link);
        return false;
    }

    private static bool Return(InstructionContext c)
    {
        LoweringState state = c.State;
        uint returnRegister = c.Bits(24, 8), index = c.Index;
        (uint? link, uint entry) = ActiveFunction(c);
        bool linked = link != null && returnRegister == link;
        for (uint prior = index; link != null && !linked && prior-- > entry;)
        {
            if (CopiesLink(state.Code[prior], returnRegister, link.Value)) linked = true;
            else if (state.Code[prior].Bits(16, 8) == returnRegister) break;
        }

        if (!state.MainExited || !linked || (c.Insn.W0 & 0x00ffffffU) != 0x00007950 || returnRegister >= 249 || (c.Insn.W2 & ~0x3ffffU) != 0x03c00000 ||
            state.BarrierTokens.Count != 0)
            return c.Fail("native return linkage mode");
        if ((long)index * 16 + 16 + BranchLowering.Offset(c) != 0) return c.Fail("native return relative base");
        c.Emit(0xe32000000007000fUL);
        bool complete = !state.ComputedBranch && state.GeometryExit == null;
        foreach (uint target in state.CallTargets) complete &= target <= index;
        return !complete || state.Finish(index);
    }
}
