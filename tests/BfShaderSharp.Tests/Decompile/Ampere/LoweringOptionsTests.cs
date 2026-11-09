using BfShaderSharp.Decompile.Ampere;

namespace BfShaderSharp.Tests.Decompile.Ampere;

public class LoweringOptionsTests
{
    [Fact]
    public void ForStageCollectsTheByteOffsetOfEveryTrap()
    {
        byte[] code = Asm.Program(Asm.Exit(), Asm.Insn(0x95c), Asm.Exit(), Asm.Insn(0x95c));
        LoweringOptions options = LoweringOptions.ForStage(code, geometry: true, TessellationStage.Control);
        Assert.Equal([16u, 48u], options.VerifiedTraps!.Order());
        Assert.True(options.Geometry);
        Assert.True(options.Tensor);
        Assert.Equal(TessellationStage.Control, options.Tessellation);
    }

    [Fact]
    public void ForStageIgnoresATrailingPartialInstruction()
    {
        LoweringOptions options = LoweringOptions.ForStage(new byte[10], geometry: false, TessellationStage.None);
        Assert.Empty(options.VerifiedTraps!);
    }
}
