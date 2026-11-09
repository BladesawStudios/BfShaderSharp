using BfShaderSharp.Decompile.Ampere;

namespace BfShaderSharp.Tests.Decompile.Ampere;

public class AmpereLowererTests
{
    private const ulong ExitWord = 0xe30000000007000f;

    [Fact]
    public void RejectsAnEmptyProgram()
    {
        LoweredProgram program = Asm.Lower();
        Assert.False(program.Succeeded);
        Assert.Equal("invalid native program size", program.Error);
    }

    [Fact]
    public void LowersAnExitToAMaxwellExit()
    {
        LoweredProgram program = Asm.Lower(Asm.Exit());
        Assert.True(program.Succeeded);
        Assert.Equal([ExitWord], program.Instructions);
        Assert.Equal(16u, program.SourceSize);
    }

    [Fact]
    public void ReportsAnUnknownOpcodeAtItsByteOffset()
    {
        LoweredProgram program = Asm.Lower(Asm.Exit(guard: 8), Asm.Insn(0xfff), Asm.Exit());
        Assert.Equal("unsupported native opcode", program.Error);
        Assert.Equal(16u, program.ErrorOffset);
    }

    [Fact]
    public void ReportsAProgramWithoutAnUnconditionalExit()
    {
        LoweredProgram program = Asm.Lower(Asm.Insn(0x918), Asm.Exit(guard: 0));
        Assert.Equal("native program has no unconditional EXIT", program.Error);
        Assert.Equal(32u, program.ErrorOffset);
    }

    [Fact]
    public void LowersAMoveImmediateThenAnExit()
    {
        AmpereInstruction move = Asm.Insn(0x802, dest: 5, w1: 0x1234, w2: 0xf00);
        LoweredProgram program = Asm.Lower(move, Asm.Exit());
        ulong expected = 0x0100000000000000UL | (7UL << 16) | 5 | (15UL << 12) | (0x1234UL << 20);
        Assert.Equal([expected, ExitWord], program.Instructions);
        Assert.Equal([0u, 1u], program.SourceInstructionOffsets);
    }

    [Fact]
    public void NamesTheReasonAnInstructionCannotBeLowered()
    {
        LoweredProgram program = Asm.Lower(Asm.Insn(0x802, dest: 5, w2: 0xf00 | (1u << 16)), Asm.Exit());
        Assert.Equal("native MOV modifiers", program.Error);
        Assert.Equal(0u, program.ErrorOffset);
    }

    [Fact]
    public void PatchesAForwardBranchWithItsDistance()
    {
        // BRA +1 instruction over a NOP to the EXIT: one lowered instruction and its scheduling slot apart.
        AmpereInstruction branch = Asm.Insn(0x947, w1: 16, w2: 0x03800000);
        LoweredProgram program = Asm.Lower(branch, Asm.Insn(0x918), Asm.Exit());
        Assert.True(program.Succeeded, program.Error);
        Assert.Equal(3, program.Instructions.Count);
        Assert.Equal(0xe24000000007000fUL | (16UL << 20), program.Instructions[0]);
    }

    [Fact]
    public void RunsTheLoweringAgainWhenHighUniformRegistersAreUsed()
    {
        // MOV R5, UR40 needs a mirror register for UR40, found by lowering a second time.
        LoweredProgram program = Asm.Lower(Asm.Insn(0xc02, dest: 5, w1: 40, w2: 0x08000f00), Asm.Exit());
        Assert.True(program.Succeeded, program.Error);
        Assert.Equal(2, program.Instructions.Count);
    }
}
