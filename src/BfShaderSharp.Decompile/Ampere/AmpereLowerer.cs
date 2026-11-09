namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers a Switch 2 (Ampere) shader program to the Maxwell instructions the Ryujinx translator reads.</summary>
/// <remarks>
/// Each source instruction becomes zero or more Maxwell instructions, so that the control flow, registers and
/// predicates of the program carry over; branch distances are patched once the whole program is placed. A program
/// that uses uniform registers above UR27 is lowered again with room reserved for mirrors of them. A program the
/// lowering does not understand yields a failed <see cref="LoweredProgram"/> naming the source instruction and the
/// reason; no exception is raised for such input. Ported from the Eden emulator's lowering (GPL-3.0-or-later) with
/// the Mesa NAK encoding tables (MIT) as its reference.
/// </remarks>
public static class AmpereLowerer
{
    private const int MaxInstructions = 65536;

    public static LoweredProgram Lower(ReadOnlySpan<byte> code, LoweringOptions options)
    {
        AmpereInstruction[] instructions = new AmpereInstruction[code.Length / 16];
        for (int i = 0; i < instructions.Length; i++) instructions[i] = new AmpereInstruction(code.Slice(i * 16, 16));
        // Programs that use uniform registers above UR27 are lowered again with mirrors for them.
        uint highUniforms = 0;
        while (true)
        {
            LoweredProgram lowered = LowerOnce(instructions, highUniforms, options);
            if (lowered.Succeeded || lowered.HighUniformRegisters <= highUniforms) return lowered;
            highUniforms = lowered.HighUniformRegisters;
        }
    }

    private static LoweredProgram LowerOnce(AmpereInstruction[] code, uint highUniforms, LoweringOptions options)
    {
        LoweringState state = new(code, highUniforms, options);
        if (code.Length == 0 || code.Length > MaxInstructions)
        {
            state.Fail(0, "invalid native program size");
            return state.Result;
        }

        if (code[0].Opcode == 0x34e && !AnalyzeGeometryExit(state)) return state.Result;
        state.Control = new ControlFlowScan(code, options.Geometry, state.UsesUniformBank, state.HighUniformBase);
        for (uint index = 0; index < code.Length; index++)
        {
            if (!LowerInstruction(state, index)) return state.Result;
            if (state.Done) return state.Result;
        }

        state.Fail((uint)code.Length, state.MainExited ? "native program lacks captured library return" : "native program has no unconditional EXIT");
        return state.Result;
    }

    private static bool AnalyzeGeometryExit(LoweringState state)
    {
        if (!state.Options.Geometry) return state.Fail(0, "native program counter load outside geometry stage");
        GeometryExitEnvelope exit = new GeometryExitAnalyzer(state.Code).Analyze();
        state.GeometryExit = exit;
        return exit.Succeeded || state.Fail(exit.ErrorOffset / 16, exit.Error!);
    }

    private static bool LowerInstruction(LoweringState state, uint index)
    {
        PrologueOutcome outcome = Prologue.Begin(state, index, out InstructionContext? context);
        if (outcome == PrologueOutcome.Skip) return true;
        if (outcome == PrologueOutcome.Failed) return false;
        InstructionContext c = context!;
        Func<InstructionContext, bool>? handler = OpcodeTable.Instance.Find(c.Opcode);
        if (handler is null) return c.Fail("unsupported native opcode");
        if (!handler(c)) return false;
        return state.Done || Prologue.Epilogue(c);
    }
}
