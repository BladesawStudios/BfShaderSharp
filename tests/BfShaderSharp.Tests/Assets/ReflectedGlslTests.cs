using BfShaderSharp.Assets;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Decompile;
using BfShaderSharp.Tests.Corpus;

namespace BfShaderSharp.Tests.Assets;

public sealed class ReflectedGlslTests
{
    private static byte[] Floats(params float[] values) => [.. values.SelectMany(BitConverter.GetBytes)];

    [Fact]
    public void ConstantReadsBecomeTheLiteralsOfTheConstantBlock()
    {
        string text = "float a = fp_c1.data[0].y + fp_c1.data[1].x;\nfloat b = vp_c1.data[0].w;\nfloat c = fp_c1.data[9].x;\n";
        string result = ReflectedGlsl.Apply(text, null, Floats(1f, 2.5f, 3f, 4f, 0.25f));
        Assert.Equal("float a = 2.5 + 0.25;\nfloat b = 4;\nfloat c = fp_c1.data[9].x;\n", result);
    }

    [Fact]
    public void AWiderSwizzleIsNotTakenForASingleRead()
    {
        string result = ReflectedGlsl.Apply("vec2 a = fp_c1.data[0].xy;\n", null, Floats(1f, 2f, 3f, 4f));
        Assert.Equal("vec2 a = fp_c1.data[0].xy;\n", result);
    }

    [Fact]
    public void SamplersAndBlocksTakeTheSlotsTheGameBindsThemAt()
    {
        string text = string.Join('\n',
            "layout (binding = 0) uniform sampler2D fp_t_tcb_C;",
            "layout (binding = 1) uniform sampler2D fp_t_tcb_A;",
            "layout (binding = 5, std140) uniform _fp_c4",
            "{",
            "    vec4 data[4096];",
            "} fp_c4;",
            "") + "\n";
        string result = ReflectedGlsl.Apply(text, null, []);
        Assert.Contains("layout (binding = 2) uniform sampler2D fp_t_tcb_C;", result);
        Assert.Contains("layout (binding = 1) uniform sampler2D fp_t_tcb_A;", result);
        Assert.Contains("layout (binding = 1, std140) uniform _fp_c4", result);
    }

    [Fact]
    public void TheConstantBlockDeclarationIsDropped()
    {
        string text = string.Join('\n',
            "layout (binding = 2, std140) uniform _fp_c1",
            "{",
            "    vec4 data[4096];",
            "} fp_c1;",
            "void main() {}",
            "") + "\n";
        Assert.Equal("void main() {}\n\n", ReflectedGlsl.Apply(text, null, []));
    }

    [BotwFact]
    public void ABotwStageIsRewrittenWithItsReflectionNames()
    {
        using RomfsFiles romfs = new(GameRomfs.Botw!);
        BfshaFile file = BfshaFile.Read(romfs.Read(romfs.ShaderFiles().Single(p => p.EndsWith("uking_mat.product.sbfsha"))));
        ShaderModel model = file.Model("uking_mat")!.Value;
        ShaderProgramBinary binary = model.Bnsh.Variation(model.Program(0).VariationIndex).Program;
        string text = ReflectedGlsl.Extract(binary.Code(ShaderStage.Vertex)!.Value, binary.Reflection(ShaderStage.Vertex));
        Assert.DoesNotContain("in_attr", text);
        Assert.DoesNotContain("c1.data[", text);
        Assert.Contains("void main()", text);
    }
}
