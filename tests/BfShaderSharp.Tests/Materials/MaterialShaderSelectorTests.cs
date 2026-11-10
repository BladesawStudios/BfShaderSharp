using System.Text;
using BfresSharp.Model.Material;
using BfShaderSharp.Lookup;
using BfShaderSharp.Materials;
using BfShaderSharp.Prepared;

namespace BfShaderSharp.Tests.Materials;

public class MaterialShaderSelectorTests
{
    private static MaterialShaderSelector Selector()
    {
        BlobPool blobs = new(quality: 4);
        LookupOption shadows = new("shadows", 0, 0b11, 0, 0, ["off", "on"]);
        LookupOption quality = new("quality", 0, 0b1100, 2, 1, ["low", "mid", "high"]);
        byte[] rows = new byte[12];
        BitConverter.TryWriteBytes(rows.AsSpan(0), 0b0100u);
        BitConverter.TryWriteBytes(rows.AsSpan(4), 0b0101u);
        BitConverter.TryWriteBytes(rows.AsSpan(8), 0b1001u);
        PreparedModelData model = new("model", [shadows, quality], rows, 1, [], [], [], [[-1, -1, -1, -1, -1, -1, -1, -1]],
            [new PreparedProgramData(0, []), new PreparedProgramData(0, []), new PreparedProgramData(0, [])]);
        using MemoryStream stream = new();
        PreparedWriter.Write(stream, [model], blobs);
        return MaterialShaderSelector.For(PreparedArchive.Read(stream.ToArray()));
    }

    private static ShaderAssign Assign(string model, params (string Option, string Choice)[] options)
    {
        ShaderAssign assign = new() { ShadingModelName = model };
        foreach ((string option, string choice) in options) assign.ShaderOptions.Add(option, choice);
        return assign;
    }

    private static int Pick(MaterialShaderSelector selector, ShaderAssign assign) => selector.Select(new Material { ShaderAssign = assign });

    [Fact]
    public void FindsTheProgramOfTheAssignedChoices()
    {
        Assert.Equal(2, Pick(Selector(), Assign("model", ("shadows", "on"), ("quality", "high"))));
        Assert.Equal(0, Pick(Selector(), Assign("model", ("shadows", "off"), ("quality", "mid"))));
    }

    [Fact]
    public void DrawTimeChoicesWinOverTheMaterial()
    {
        MaterialShaderSelector selector = Selector();
        Material material = new() { ShaderAssign = Assign("model", ("shadows", "on"), ("quality", "mid")) };
        Assert.Equal(1, selector.Select(material));
        Assert.Equal(2, selector.Select(material, new Dictionary<string, string> { ["quality"] = "high" }));
    }

    [Fact]
    public void AMissingCombinationGetsTheNearestProgramKeepingDrawTimeChoices()
    {
        // No program has shadows off with quality high.
        MaterialShaderSelector selector = Selector();
        Material material = new() { ShaderAssign = Assign("model", ("shadows", "off"), ("quality", "high")) };
        Assert.Equal(0, selector.Select(material));
        Assert.Equal(2, selector.Select(material, new Dictionary<string, string> { ["quality"] = "high" }));
    }

    [Fact]
    public void DefaultValuesTakeTheShadersDefaultChoice()
    {
        Assert.Equal(1, Pick(Selector(), Assign("model", ("shadows", "on"), ("quality", ShaderAssign.DefaultValue))));
    }

    [Fact]
    public void ChoicesOfSkipsOptionsLeftAtTheirDefault()
    {
        Dictionary<string, string> choices = MaterialShaderSelector.ChoicesOf(Assign("model", ("a", "x"), ("b", ShaderAssign.DefaultValue)), new Dictionary<ulong, string>());
        Assert.Equal(new Dictionary<string, string> { ["a"] = "x" }, choices);
    }

    [Fact]
    public void ResolvesHashedKeysThroughTheNameTable()
    {
        Dictionary<ulong, string> names = new() { [0x00000000000000ABul] = "shadows" };
        Dictionary<string, string> choices = MaterialShaderSelector.ChoicesOf(
            Assign("model", ("0x00000000000000AB", "on"), ("0x00000000000000CD", "x"), ("quality", "high")), names);
        Assert.Equal(new Dictionary<string, string> { ["shadows"] = "on", ["quality"] = "high" }, choices);
    }

    [Fact]
    public void BooleanValuesAreTheShadersZeroAndOne()
    {
        Dictionary<string, string> choices = MaterialShaderSelector.ChoicesOf(Assign("model", ("a", "True"), ("b", "False"), ("c", "3")), new Dictionary<ulong, string>());
        Assert.Equal(new Dictionary<string, string> { ["a"] = "1", ["b"] = "0", ["c"] = "3" }, choices);
    }

    [Fact]
    public void RenderInfosSetTheRenderStateOptions()
    {
        Material material = new() { ShaderAssign = Assign("model") };
        AddInfo(material, "gsys_render_state_mode", "translucent");
        AddInfo(material, "gsys_alpha_test_func", "gequal");
        AddInfo(material, "gsys_pass", "unheard_of");
        AddInfo(material, "gsys_priority_hint", "npc");
        Dictionary<string, string> choices = MaterialShaderSelector.ChoicesOf(material, new Dictionary<ulong, string>());
        Assert.Equal(new Dictionary<string, string> { ["gsys_renderstate"] = "2", ["gsys_alpha_test_func"] = "6" }, choices);
    }

    private static void AddInfo(Material material, string name, string word)
    {
        RenderInfo info = new() { Name = name };
        info.SetValue([word]);
        material.RenderInfos.Add(name, info);
    }

    [Fact]
    public void ReportsNothingForAnUnknownModelOrChoice()
    {
        MaterialShaderSelector selector = Selector();
        Assert.Equal(-1, Pick(selector, Assign("other")));
        Assert.Equal(-1, Pick(selector, Assign("model", ("shadows", "maybe"))));
    }
}
