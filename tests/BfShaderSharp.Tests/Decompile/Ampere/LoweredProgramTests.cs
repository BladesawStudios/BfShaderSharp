using BfShaderSharp.Decompile.Ampere;

namespace BfShaderSharp.Tests.Decompile.Ampere;

public class LoweredProgramTests
{
    [Fact]
    public void ANewProgramHasSucceededAndNeedsNoTextureBuffer()
    {
        var program = new LoweredProgram();
        Assert.True(program.Succeeded);
        Assert.Equal(uint.MaxValue, program.RequiredTextureBuffer);
    }

    [Fact]
    public void FailingClearsTheOutputAndNamesTheByteOffset()
    {
        var program = new LoweredProgram();
        program.Instructions.Add(1);
        program.SourceInstructionOffsets.Add(0);
        program.Fail(3, "reason");
        Assert.False(program.Succeeded);
        Assert.Empty(program.Instructions);
        Assert.Empty(program.SourceInstructionOffsets);
        Assert.Equal(48u, program.ErrorOffset);
        Assert.Equal("reason", program.Error);
    }
}
