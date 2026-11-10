using BfShaderSharp.Decompile.Ampere;

namespace BfShaderSharp.Tests.Decompile.Ampere;

public class StageBufferLoweringTests
{
    private const uint ReadFlags = 0xb800;

    [Fact]
    public void AnOutputReadBecomesTheStageBufferPseudoInstruction()
    {
        LoweredProgram program = Asm.Lower(Asm.Insn(0x923, dest: 5, w1: 0x1000 << 8, w2: ReadFlags), Asm.Exit());
        Assert.True(program.Succeeded, program.Error);
        ulong word = program.Instructions[0];
        Assert.Equal(0xefc0UL >> 1, word >> 49);
        Assert.Equal(5UL, word & 0xff);
        Assert.Equal(0x1000UL, word >> 20 & 0xffffff);
        Assert.Equal(1UL, word >> 47 & 1);
        Assert.Equal(0UL, word >> 48 & 1);
    }

    [Fact]
    public void AWriteSetsTheWriteFlagAndKeepsTheDataRegister()
    {
        LoweredProgram program = Asm.Lower(Asm.Insn(0x927, w1: 0x04 << 8 | 7, w2: ReadFlags), Asm.Exit());
        Assert.True(program.Succeeded, program.Error);
        ulong word = program.Instructions[0];
        Assert.Equal(7UL, word & 0xff);
        Assert.Equal(4UL, word >> 20 & 0xffffff);
        Assert.Equal(1UL, word >> 48 & 1);
    }

    [Fact]
    public void AWriteToTheInputRegionIsRejected()
    {
        LoweredProgram program = Asm.Lower(Asm.Insn(0x927, w1: 4 << 8, w2: ReadFlags & ~0x8000U), Asm.Exit());
        Assert.False(program.Succeeded);
        Assert.Equal("native stage buffer access mode", program.Error);
    }
}
