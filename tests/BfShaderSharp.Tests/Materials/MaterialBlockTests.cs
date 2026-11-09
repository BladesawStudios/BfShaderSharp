using System.Buffers.Binary;
using BfresSharp.Model.Material;
using BfShaderSharp.Materials;

namespace BfShaderSharp.Tests.Materials;

public class MaterialBlockTests
{
    private static Material MaterialWith(params (string Name, ShaderParamType Type, float[] Values)[] parameters)
    {
        Material material = new() { Name = "m" };
        List<byte> data = [];
        foreach ((string name, ShaderParamType type, float[] values) in parameters)
        {
            material.ShaderParams.Add(name, new ShaderParam { Name = name, Type = type, DataOffset = (ushort)data.Count });
            foreach (float value in values) data.AddRange(BitConverter.GetBytes(value));
            while (data.Count < material.ShaderParams[name].DataOffset + material.ShaderParams[name].DataSize) data.Add(0);
        }

        material.ShaderParamData = [.. data];
        return material;
    }

    private static float At(byte[] block, int offset) => BinaryPrimitives.ReadSingleLittleEndian(block.AsSpan(offset));

    [Fact]
    public void ParametersAreWrittenAtTheOffsetsTheShaderGivesTheirNamesOverTheDefaults()
    {
        MaterialBlockLayout layout = new("gsys_material", 64, [.. Enumerable.Repeat((byte)7, 64)], [("a", 16), ("b", 48)]);
        byte[] block = layout.Build(MaterialWith(("b", ShaderParamType.Float2, [1.5f, 2.5f]), ("unknown", ShaderParamType.Float, [9f])), key => key);
        Assert.Equal(64, block.Length);
        Assert.Equal(1.5f, At(block, 48));
        Assert.Equal(2.5f, At(block, 52));
        Assert.Equal(7, block[0]);
        Assert.Equal(7, block[16]);
    }

    [Fact]
    public void AModeZeroTransformOfNoScaleRotationOrTranslationIsTheIdentity()
    {
        byte[] baked = TexSrtBake.Bake([.. new float[] { 0, 1, 1, 0, 0, 0 }.SelectMany(BitConverter.GetBytes)]);
        float[] values = [.. Enumerable.Range(0, 6).Select(i => At(baked, i * 4))];
        Assert.Equal([1f, 0f, 0f, 1f, 0f, 0f], values.Select(v => MathF.Abs(v) < 1e-6f ? 0f : MathF.Round(v, 5)));
    }

    [Fact]
    public void ATransformIsBakedWhereTheShaderReadsIt()
    {
        MaterialBlockLayout layout = new("gsys_material", 32, [], [("tex", 0)]);
        byte[] block = layout.Build(MaterialWith(("tex", ShaderParamType.TexSrt, [0, 2, 3, 0, 0.25f, 0.5f])), key => key);
        Assert.Equal(2f, At(block, 0));
        Assert.Equal(3f, At(block, 12));
    }
}
