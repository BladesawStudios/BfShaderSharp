using System.Text;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Errors;
using BfShaderSharp.Lookup;
using BfShaderSharp.Prepared;

namespace BfShaderSharp.Tests.Prepared;

public class PreparedRoundTripTests
{
    private static byte[] Rows(params uint[] words)
    {
        byte[] bytes = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++) BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), words[i]);
        return bytes;
    }

    private static (byte[] File, int Vertex, int Fragment, int Default) Build()
    {
        BlobPool blobs = new(quality: 4);
        int vertex = blobs.Add(Encoding.UTF8.GetBytes("void main() { /* vertex */ }"));
        int fragment = blobs.Add(Encoding.UTF8.GetBytes("void main() { /* fragment */ }"));
        int defaults = blobs.Add([1, 2, 3, 4]);
        LookupOption shadows = new("shadows", 0, 0b11, 0, 0, ["off", "on"]);
        LookupOption quality = new("quality", 0, 0b1100, 2, 1, ["low", "mid", "high"]);
        PreparedModelData model = new("model", [shadows, quality], Rows(0b0100, 0b0101, 0b1001), 1,
            [new PreparedSampler("_a0", "albedo", 0)], [new PreparedAttribute("position", 0, 3)],
            [new PreparedBlock("gsys_material", 2, 1, 16, [new PreparedUniform("tint", 0, 4, 2)], defaults)],
            [[vertex, -1, -1, -1, fragment, -1], [-1, -1, -1, -1, fragment, -1]],
            [new PreparedProgramData(0, [new PreparedBinding(1, -1), new PreparedBinding(-1, 2)]),
             new PreparedProgramData(0, [new PreparedBinding(3, 4), new PreparedBinding(5, 6)]),
             new PreparedProgramData(1, [new PreparedBinding(-1, -1), new PreparedBinding(7, 8)])]);
        using MemoryStream stream = new();
        PreparedWriter.Write(stream, [model], blobs);
        return (stream.ToArray(), vertex, fragment, defaults);
    }

    [Fact]
    public void ReadsBackWhatWasWritten()
    {
        var (file, vertex, fragment, defaults) = Build();
        PreparedArchive archive = PreparedArchive.Read(file);
        PreparedModel model = archive.Model("model")!;
        Assert.Equal(3, model.ProgramCount);
        Assert.Equal(vertex, model.StageBlob(0, ShaderStage.Vertex));
        Assert.Equal(-1, model.StageBlob(1, ShaderStage.Vertex));
        Assert.Equal("void main() { /* fragment */ }", model.Glsl(2, ShaderStage.Fragment));
        Assert.Null(model.Glsl(2, ShaderStage.Vertex));
        Assert.Equal(new PreparedBinding(3, 4), model.Program(1).Sampler(0));
        Assert.Equal(new PreparedBinding(5, 6), model.Program(1).UniformBlock(0));
        Assert.Equal(new PreparedBinding(7, 8), model.Program(2).UniformBlock(0));
        Assert.Equal(new PreparedSampler("_a0", "albedo", 0), model.Samplers()[0]);
        Assert.Equal(new PreparedAttribute("position", 0, 3), model.Attributes()[0]);
        PreparedBlock block = model.Blocks()[0];
        Assert.Equal("tint", block.Uniforms[0].Name);
        Assert.Equal([1, 2, 3, 4], archive.Blob(block.DefaultBuffer));
        Assert.Equal(fragment, model.StageBlob(0, ShaderStage.Fragment));
        Assert.Equal(defaults, block.DefaultBuffer);
    }

    [Fact]
    public void StoresIdenticalBlobsOnce()
    {
        BlobPool blobs = new(quality: 4);
        Assert.Equal(blobs.Add([9, 9, 9]), blobs.Add([9, 9, 9]));
        Assert.NotEqual(blobs.Add([9, 9, 9]), blobs.Add([9, 9, 8]));
        Assert.Equal(2, blobs.Count);
    }

    [Fact]
    public void LookupFindsProgramsFromStoredKeys()
    {
        PreparedModel model = PreparedArchive.Read(Build().File).Model(0);
        ProgramLookup lookup = model.CreateLookup();
        Assert.Equal(0, lookup.Find(new Dictionary<string, string> { ["shadows"] = "off", ["quality"] = "mid" }));
        Assert.Equal(1, lookup.Find(new Dictionary<string, string> { ["shadows"] = "on", ["quality"] = "mid" }));
        Assert.Equal(2, lookup.Find(new Dictionary<string, string> { ["shadows"] = "on", ["quality"] = "high" }));
        Assert.Equal(-1, lookup.Find(new Dictionary<string, string> { ["shadows"] = "maybe" }));
        Assert.Equal(1, lookup.Find(new Dictionary<string, string> { ["shadows"] = "on" }));
    }

    [Fact]
    public void RejectsBytesThatAreNotAPreparedFile()
    {
        Assert.Throws<BfShaderFormatException>(() => PreparedArchive.Read(new byte[0x40]));
        byte[] truncated = Build().File[..0x80];
        Assert.Throws<BfShaderFormatException>(() => PreparedArchive.Read(truncated));
    }

    [Fact]
    public void RejectsBlobIndexesOutsideTheFile()
    {
        PreparedArchive archive = PreparedArchive.Read(Build().File);
        Assert.Throws<BfShaderFormatException>(() => archive.Blob(99));
        Assert.Throws<BfShaderFormatException>(() => archive.String(99));
    }
}
