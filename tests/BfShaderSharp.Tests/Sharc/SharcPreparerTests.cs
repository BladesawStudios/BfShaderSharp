using BfShaderSharp.Preparation;
using BfShaderSharp.Prepared;
using BfShaderSharp.Sharc;

namespace BfShaderSharp.Tests.Sharc;

public class SharcPreparerTests
{
    private static (PreparationResult Result, PreparedArchive Archive) Prepare(SharcbFile file)
    {
        using MemoryStream output = new();
        PreparationResult result = SharcPreparer.Prepare(file, output, compressionQuality: 3, parallelism: 1);
        return (result, PreparedArchive.Read(output.ToArray()));
    }

    [Fact]
    public void ProgramsBecomeModelsAndCombinationsBecomePrograms()
    {
        (PreparationResult result, PreparedArchive archive) = Prepare(SharcbFile.Read(SharcBytes.Sharc()));
        Assert.Equal((1, 2, 4), (result.Models, result.Programs, result.Stages));
        PreparedModel model = archive.Model("prog")!;
        Assert.Equal(2, model.ProgramCount);
        Assert.Equal("tex0", Assert.Single(model.Samplers()).Name);
        Assert.Equal(new PreparedBinding(3, -1), model.Program(0).Sampler(0));
        Assert.Equal(new PreparedBinding(-1, -1), model.Program(1).Sampler(0));
    }

    [Fact]
    public void TheLookupFindsACombinationByOptionValue()
    {
        PreparedModel model = Prepare(SharcbFile.Read(SharcBytes.Sharc())).Archive.Model("prog")!;
        var lookup = model.CreateLookup();
        Assert.Equal(1, lookup.Find(new Dictionary<string, string> { ["A"] = "on" }));
        Assert.Equal(0, lookup.Find(new Dictionary<string, string>()));
    }

    [Fact]
    public void StagesThatCannotBeDecompiledAreReportedNotHidden()
    {
        PreparationResult result = Prepare(SharcbFile.Read(SharcBytes.Sharc())).Result;
        Assert.Equal(4, result.Failures.Count);
    }
}
