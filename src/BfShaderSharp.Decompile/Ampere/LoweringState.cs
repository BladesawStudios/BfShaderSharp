namespace BfShaderSharp.Decompile.Ampere;

/// <summary>A branch or call whose target is not known until the whole program has been lowered.</summary>
/// <remarks>
/// <see cref="OutputIndex"/> is the lowered instruction to patch and <see cref="Target"/> the source instruction it
/// goes to. When the branch must land on the register copies that follow a source instruction instead,
/// <see cref="OutputTarget"/> holds that lowered position.
/// </remarks>
internal sealed class Relocation(uint outputIndex, long target, bool call = false)
{
    public uint OutputIndex { get; } = outputIndex;

    public long Target { get; } = target;

    public bool Call { get; } = call;

    public uint OutputTarget { get; set; } = uint.MaxValue;
}

/// <summary>Everything the lowering of one program carries from instruction to instruction.</summary>
/// <remarks>
/// Holds the source code, the result being built, the branches waiting for their targets, the call and barrier
/// bookkeeping, and the facts found by the program scan. The lowered program is laid out so that instruction
/// <c>i</c> sits at the address Maxwell scheduling words put it at, which is what <see cref="AddressOf"/> computes.
/// A failure clears the output and records the source instruction and reason.
/// </remarks>
internal sealed class LoweringState
{
    public LoweringState(AmpereInstruction[] code, uint highUniforms, LoweringOptions options)
    {
        Code = code;
        Options = options;
        HighUniforms = highUniforms;
        HighUniformBase = 216 - highUniforms;
        UsesAsyncCopy = code.Any(i => i.Opcode == 0xfae);
        UsesUniformBank = code.Any(OpcodeFacts.ReadsUniformBank);
    }

    public AmpereInstruction[] Code { get; }

    public LoweringOptions Options { get; }

    public LoweredProgram Result { get; } = new();

    public uint HighUniforms { get; }

    // UR28 and up (when used) live just below R216: UR28 at R(216 - HighUniforms).
    public uint HighUniformBase { get; }

    public bool UsesAsyncCopy { get; }

    public bool UsesUniformBank { get; }

    public List<Relocation> Relocations { get; } = [];

    public List<uint> BarrierTokens { get; } = [];

    public List<uint> CallTargets { get; } = [];

    public List<uint> CallLinks { get; } = [];

    public bool MainExited { get; set; }

    // A jump table: the program ends at its self-branch, not an EXIT.
    public bool ComputedBranch { get; set; }

    public bool Done { get; private set; }

    public GeometryExitEnvelope? GeometryExit { get; set; }

    public ControlFlowScan Control { get; set; } = null!;

    public static uint AddressOf(uint index) => 0x50U + index * 8 + (index + 1) / 3 * 8;

    public bool Fail(uint index, string reason)
    {
        Result.Fail(index, reason);
        Done = true;
        return false;
    }

    // Patches every pending branch with its relative distance and completes the program at the given source instruction.
    public bool Finish(uint index)
    {
        foreach (Relocation relocation in Relocations)
        {
            if (relocation.Target > index) return Fail(index, "native branch or call leaves captured program");
            uint target = relocation.OutputTarget != uint.MaxValue ? relocation.OutputTarget : Result.SourceInstructionOffsets[(int)relocation.Target];
            long relative = (long)AddressOf(target) - AddressOf(relocation.OutputIndex) - 8;
            if (relative < -0x800000 || relative > 0x7fffff) return Fail(index, "lowered branch distance");
            Result.Instructions[(int)relocation.OutputIndex] |= ((ulong)relative & 0xffffff) << 20;
        }

        Result.SourceSize = (index + 1) * 16;
        Done = true;
        return true;
    }
}
