namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the program exit (EXIT) and decides when the whole program has been seen.</summary>
/// <remarks>
/// An exit before a geometry stage's exit handler branches to the handler instead. A conditional exit stays one. An
/// unconditional exit ends the program only when no branch still jumps past it and no function remains to be placed;
/// otherwise it ends one path of the main program, or marks the end of the main program and the start of the functions
/// that its calls reach. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class ExitLowering
{
    public static void Register(OpcodeTable table) => table.Add(Exit, 0x94d);

    private static bool Exit(InstructionContext c)
    {
        LoweringState state = c.State;
        uint index = c.Index;
        if (state.GeometryExit is { } handler && (ulong)index * 16 < handler.HandlerOffset) return BranchToHandler(c, handler);
        if (c.Bits(84, 2) != 0) return c.Fail("native conditional/special EXIT");
        if (c.Bits(87, 4) != 7)
        {
            // @Pg EXIT Pc: exits when both hold; skip over a Pc-predicated EXIT unless Pg.
            if (c.NativePredicate != 7)
            {
                state.Relocations.Add(new Relocation((uint)c.Output.Count, index + 1));
                c.Emit(0xe24000000000000fUL | ((ulong)(c.NativePredicate ^ 8U) << 16));
            }

            c.Emit(0xe30000000000000fUL | ((ulong)c.Bits(87, 4) << 16));
            return true;
        }

        c.Emit(0xe30000000000000fUL | c.Predicate);
        if (c.Bits(12, 4) != 7) return true;
        if (state.BarrierTokens.Count != 0) return c.Fail("native unclosed reconvergence scope");
        if (state.CallTargets.Count == 0)
        {
            bool pending = state.Relocations.Any(r => r.Target > index);
            return pending || state.ComputedBranch || state.Finish(index);
        }

        return AfterFunctions(c);
    }

    private static bool BranchToHandler(InstructionContext c, GeometryExitEnvelope handler)
    {
        LoweringState state = c.State;
        long handlerIndex = handler.HandlerOffset / 16;
        state.Relocations.Add(new Relocation((uint)c.Output.Count, handlerIndex));
        c.Emit(0xe24000000000000fUL | c.Predicate);
        bool pendingMain = state.Relocations.Any(r => !r.Call && r.Target > c.Index && r.Target != handlerIndex);
        if (!handler.HasReturns || c.NativePredicate != 7 || state.MainExited || pendingMain || state.CallTargets.Count == 0) return true;
        if (state.CallTargets.Any(target => target <= c.Index)) return c.Fail("native call target overlaps main program");
        state.MainExited = true;
        return true;
    }

    // An exit in a program with functions: it ends one path of the main program, ends the main program, or ends the whole program.
    private static bool AfterFunctions(InstructionContext c)
    {
        LoweringState state = c.State;
        uint index = c.Index;
        bool pendingMain = state.Relocations.Any(r => !r.Call && r.Target > index);
        if (!state.MainExited && pendingMain) return true;
        if (state.MainExited)
        {
            // A library function may end the shader instead of returning; calls are inlined downstream, so this is an ordinary exit there.
            bool complete = !state.Relocations.Any(r => r.Target > index) && state.CallTargets.All(target => target <= index);
            return !(complete && !state.ComputedBranch) || state.Finish(index);
        }

        if (state.CallTargets.Any(target => target <= index)) return c.Fail("native call target overlaps main program");
        // The main program goes on past an EXIT that a branch still jumps over.
        bool pending = state.Relocations.Any(r => r.Target > index && !state.CallTargets.Contains((uint)r.Target));
        if (!pending) state.MainExited = true;
        return true;
    }
}
