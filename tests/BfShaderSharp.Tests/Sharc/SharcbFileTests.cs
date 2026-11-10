using BfShaderSharp.Errors;
using BfShaderSharp.Sharc;

namespace BfShaderSharp.Tests.Sharc;

public class SharcbFileTests
{
    private static SharcbFile File() => SharcbFile.Read(SharcBytes.Sharc());

    [Fact]
    public void ReadsTheNameProgramsAndOptions()
    {
        SharcbFile file = File();
        Assert.Equal("lib", file.Name);
        Assert.Equal(11, file.Version);
        SharcProgram program = Assert.Single(file.Programs);
        Assert.Equal("prog", program.Name);
        Assert.Equal(2, program.StageCount);
        Assert.Equal([SharcStage.Vertex, SharcStage.Pixel], program.Stages);
        SharcMacro macro = Assert.Single(program.Macros);
        Assert.Equal("A", macro.Name);
        Assert.Equal(["off", "on"], macro.Values);
        Assert.Equal(2, program.VariationCount);
    }

    [Fact]
    public void FindsTheStageOfAVariationByOptionValue()
    {
        SharcbFile file = File();
        SharcProgram program = file.Programs[0];
        int on = program.VariationIndex(new Dictionary<string, string> { ["A"] = "on" });
        Assert.Equal(1, on);
        Assert.Equal(0, program.VariationIndex(new Dictionary<string, string>()));
        SharcVariation pixel = file.Variation(program, on, SharcStage.Pixel);
        Assert.Equal(SharcStage.Pixel, pixel.Stage);
        Assert.Equal(0x80, pixel.ByteCode.Length);
        Assert.Equal(0x13, pixel.ByteCode.Span[0]);
        Assert.Throws<ArgumentException>(() => program.VariationIndex(new Dictionary<string, string> { ["A"] = "maybe" }));
        Assert.Throws<BfShaderFormatException>(() => file.Variation(program, 0, SharcStage.Compute));
    }

    [Fact]
    public void ReadsTheBindingsOfAStage()
    {
        SharcVariation first = File().Variations[0];
        SharcSymbol sampler = Assert.Single(first.Samplers);
        Assert.Equal(new SharcSymbol("tex0", 3), sampler);
        Assert.Empty(first.Blocks);
        Assert.Empty(File().Variations[1].Samplers);
    }

    [Fact]
    public void EveryStageKindHasStageCodeAndAnUnknownKindDoesNot()
    {
        SharcVariation first = File().Variations[0];
        Assert.Equal(BfShaderSharp.Bnsh.ShaderStage.Vertex, first.ToStageCode().Stage);
        Assert.Throws<BfShaderUnsupportedException>(() => (first with { Stage = (SharcStage)7 }).ToStageCode());
        Assert.Equal(BfShaderSharp.Bnsh.ShaderStage.Mesh, (first with { Stage = SharcStage.Mesh }).ToStageCode().Stage);
    }

    [Fact]
    public void RejectsOtherMagicOldVersionsAndTruncatedFiles()
    {
        byte[] bytes = SharcBytes.Sharc();
        Assert.Throws<BfShaderFormatException>(() => SharcbFile.Read(new byte[64]));
        byte[] old = (byte[])bytes.Clone();
        old[4] = 5;
        Assert.Throws<BfShaderUnsupportedException>(() => SharcbFile.Read(old));
        Assert.Throws<BfShaderFormatException>(() => SharcbFile.Read(bytes.AsMemory(0, 0x150 + SharcBytes.Alignment + 40)));
    }
}
