using BfShaderSharp.Assets;

namespace BfShaderSharp.Tests.Assets;

public class ProgramUseTests
{
    [Fact]
    public void ProgramsAreGroupedByArchiveAndModel()
    {
        ProgramUse use = new();
        use.Add("Model", "model", 3);
        use.Add("Model", "model", 3);
        use.Add("Model", "model", 9);
        use.Add("Model", "other", 1);
        use.Add("Terrain", "ground", 5);
        Assert.Equal(4, use.Count);
        IReadOnlyDictionary<string, IReadOnlySet<int>> model = use.Of("Model");
        Assert.Equal([3, 9], model["model"].Order());
        Assert.Equal([1], model["other"]);
        Assert.Empty(use.Of("Nothing"));
    }

    [Fact]
    public void ANegativeProgramIsNotUse()
    {
        ProgramUse use = new();
        use.Add("Model", "model", -1);
        Assert.Equal(0, use.Count);
    }

    [Fact]
    public void ManyThreadsRecordEachProgramOnce()
    {
        ProgramUse use = new();
        Parallel.For(0, 1000, i => use.Add("A", "m", i % 10));
        Assert.Equal(10, use.Count);
    }
}
