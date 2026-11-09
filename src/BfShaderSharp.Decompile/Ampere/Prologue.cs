namespace BfShaderSharp.Decompile.Ampere;

/// <summary>What happens to a source instruction before its opcode handler runs.</summary>
/// <remarks>
/// An instruction is skipped, rejected, or prepared: <see cref="Prologue.Begin"/> returns the context the handler works
/// in.
/// </remarks>
internal enum PrologueOutcome
{
    Skip,
    Failed,
    Ready,
}

/// <summary>Prepares one source instruction for lowering: skips dead ones, remaps high registers, rewrites uniform arithmetic and sets up guards.</summary>
/// <remarks>
/// Records where the instruction's output starts, drops instructions that never execute or belong to a replaced
/// geometry exit, rejects callee code that overwrites a call's return register, shadows registers above the lowering's
/// scratch range, and swaps a uniform guard into an ordinary predicate for the duration of the instruction.
/// <see cref="Epilogue"/> undoes the swap. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class Prologue
{
    public static PrologueOutcome Begin(LoweringState state, uint index, out InstructionContext? context)
    {
        context = null;
        AmpereInstruction instruction = state.Code[index];
        state.Result.SourceInstructionOffsets.Add((uint)state.Result.Instructions.Count);
        if (state.Control.DeadInstructions[index]) return PrologueOutcome.Skip;
        GeometryExitEnvelope? exit = state.GeometryExit;
        if (exit is not null && (index < GeometryExitAnalyzer.SetupInstructions || index * 16 == exit.HandlerOffset + 16)) return PrologueOutcome.Skip;
        if (instruction.Bits(12, 4) == 15) return PrologueOutcome.Skip; // @!PT: never executes (compiler padding)
        if (!CheckCalleeLink(state, index, instruction)) return PrologueOutcome.Failed;
        RegisterPressure pressure = RegisterPressure.None(instruction);
        if (RequiresShadow(state, instruction))
        {
            if (!state.UsesUniformBank || (state.HighUniformBase & 3) != 0) return Reject(state, index, "native program uses lowering scratch registers");
            pressure = RegisterPressure.Prepare(instruction, state.HighUniformBase);
            if (!pressure.Valid) return Reject(state, index, "native register shadowing mode");
            instruction = pressure.Instruction;
            state.Result.Instructions.AddRange(pressure.Prefix);
        }

        InstructionContext c = new(state, index, instruction) { Pressure = pressure };
        c.InstructionStart = state.Result.Instructions.Count;
        c.RelocationStart = state.Relocations.Count;
        context = c;
        if (UniformArithmeticRewrite.Applies(c.Opcode) && !UniformArithmeticRewrite.Apply(c)) return PrologueOutcome.Failed;
        SetUpGuards(c);
        return PrologueOutcome.Ready;
    }

    private static PrologueOutcome Reject(LoweringState state, uint index, string reason)
    {
        state.Fail(index, reason);
        return PrologueOutcome.Failed;
    }

    // Once the main program has exited, only library functions remain; one that overwrites the register its caller keeps the return address in is rejected.
    private static bool CheckCalleeLink(LoweringState state, uint index, AmpereInstruction instruction)
    {
        GeometryExitEnvelope? exit = state.GeometryExit;
        if (!state.MainExited || (exit is not null && index * 16 >= exit.HandlerOffset)) return true;
        uint? link = null;
        uint entry = 0;
        for (int call = 0; call < state.CallTargets.Count; call++)
        {
            if (state.CallTargets[call] > index || state.CallTargets[call] < entry) continue;
            entry = state.CallTargets[call];
            link = state.CallLinks[call];
        }

        // RET, BRA, NOP and ISETP/FSETP (predicate outputs) have no destination GPR; bits 16..23 are not a write to R0. IMAD.WIDE writes a register pair.
        uint opcode = instruction.Opcode;
        bool noDestination = opcode == 0x950 || opcode == 0x947 || opcode == 0x918 || (opcode & 0x1ff) == 0x00c || (opcode & 0x1ff) == 0x00b;
        uint written = OpcodeFacts.WriteCount(instruction) ?? 1;
        if (link is null || noDestination) return true;
        uint destination = instruction.Bits(16, 8);
        return destination > link || link >= destination + written || state.Fail(index, "native callee overwrites link register");
    }

    private static bool RequiresShadow(LoweringState state, AmpereInstruction instruction)
    {
        uint opcode = instruction.Opcode;
        foreach (int bit in new[] { 16, 24, 32, 64 })
        {
            if (opcode == 0x81c) break;
            if (bit == 32 && instruction.Bits(9, 3) != 1 && opcode != 0xb60 && opcode != 0xb66 && opcode != 0x986 && opcode != 0x987 && opcode != 0xf66 && opcode != 0x988) continue;
            uint register = instruction.Bits(bit, 8);
            if (register >= (state.UsesUniformBank ? state.HighUniformBase : 246U) && register < 255) return true;
        }

        return false;
    }

    private static void SetUpGuards(InstructionContext c)
    {
        // 0x899 (USHF) lowers to one SHR/SHL, so a uniform guard is swapped in like these.
        uint opcode = c.Opcode;
        bool uniformPredication = opcode is 0xabb or 0xab9 or 0xc82 or 0x882 or 0x899 or 0x299 or 0x499;
        c.NativePredicate = c.Bits(12, 4);
        c.GuardedUniform = (uniformPredication || c.UniformGuard) && c.NativePredicate != 7;
        // A uniform guard UPn lives in R239 bit 6-n and is swapped into P(6-n) for the instruction.
        c.GuardSlot = 6 - (c.NativePredicate & 7);
        c.Predicate = (ulong)(c.GuardedUniform ? (c.NativePredicate & 8) | c.GuardSlot : c.NativePredicate) << 16;
        c.Dest = c.Bits(16, 8);
        // P(guard slot) while a uniform guard is swapped in; P6 for lowerings that use it as scratch, which save and restore it themselves.
        c.GuardMask = c.GuardSlot < 7 ? 1U << (int)c.GuardSlot : 0U;
        if (c.GuardedUniform)
        {
            c.SavePredicate(c.GuardMask);
            c.Emit(0x38f0000000070000UL | (239UL << 8) | ((ulong)c.GuardMask << 20));
        }

        c.OutputMask = c.UniformOutput == 7 ? 0U : 1U << (int)(6 - c.UniformOutput);
        if (c.OutputMask == 0) return;
        // Keep the guest P(6-n) in R254 and start from the current UPn (a false guard keeps it).
        c.Emit(0x38e8000000070000UL | 254 | (255UL << 8) | ((ulong)c.OutputMask << 20));
        c.Emit(0x38f0000000070000UL | (239UL << 8) | ((ulong)c.OutputMask << 20));
    }

    // Undoes the guard swap and emits the register copies that follow an instruction.
    public static bool Epilogue(InstructionContext c)
    {
        if (c.OperandError is not null) return c.Fail(c.OperandError);
        if (c.OutputMask != 0)
        {
            c.Emit(0x38e8000000070000UL | 239 | (239UL << 8) | ((ulong)c.OutputMask << 20));
            c.Emit(0x38f0000000070000UL | (254UL << 8) | ((ulong)c.OutputMask << 20));
        }

        if (c.GuardedUniform) c.RestorePredicate(c.GuardMask);
        if (c.Pressure.Suffix.Count == 0) return true;
        // Branches that skip the rest of this instruction land on its copies back.
        for (int r = c.RelocationStart; r < c.State.Relocations.Count; r++)
        {
            Relocation pending = c.State.Relocations[r];
            if (!pending.Call && pending.OutputIndex >= c.InstructionStart && pending.Target == c.Index + 1)
                pending.OutputTarget = (uint)c.Output.Count;
        }

        c.Output.AddRange(c.Pressure.Suffix);
        return true;
    }
}
