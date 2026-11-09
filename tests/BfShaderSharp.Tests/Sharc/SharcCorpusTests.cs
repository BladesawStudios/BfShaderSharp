using BfShaderSharp.Decompile;
using BfShaderSharp.Errors;
using BfShaderSharp.Sharc;
using BfShaderSharp.Tests.Corpus;
using SarcSharp;

namespace BfShaderSharp.Tests.Sharc;

public class SharcCorpusTests
{
    private static IEnumerable<SharcbFile> Files() =>
        GameFiles.Folders.SelectMany(GameFiles.ApplicationPackages)
            .Select(bytes => SarcFile.FromBinary(bytes))
            .SelectMany(sarc => sarc.Entries.Where(e => e.Name.EndsWith(".sharcb")).Select(e => SharcbFile.Read(e.Data)));

    [CorpusFact]
    public void EveryProgramIndexesStagesOfItsOwnKind()
    {
        int programs = 0;
        foreach (SharcbFile file in Files())
        {
            foreach (SharcProgram program in file.Programs)
            {
                programs++;
                int last = program.BaseIndex + program.VariationCount * program.StageCount - 1;
                Assert.True(last < file.Variations.Count, $"{program.Name} runs past the stage list");
                foreach (int variation in new[] { 0, program.VariationCount - 1 })
                    foreach (SharcStage stage in program.Stages)
                        Assert.Equal(stage, file.Variation(program, variation, stage).Stage);
            }
        }

        Assert.True(programs > 0);
    }

    [CorpusFact]
    public void SampledStagesDecompile()
    {
        int tried = 0;
        int decompiled = 0;
        foreach (SharcbFile file in Files())
        {
            foreach (SharcProgram program in file.Programs.Where((_, i) => i % 8 == 0))
            {
                foreach (SharcStage stage in program.Stages.Where(s => s <= SharcStage.Compute))
                {
                    tried++;
                    try
                    {
                        Assert.Contains("void main", ShaderDecompiler.ToGlsl(file.Variation(program, 0, stage).ToStageCode()));
                        decompiled++;
                    }
                    catch (BfShaderFormatException)
                    {
                    }
                }
            }
        }

        Assert.True(tried > 0);
        Assert.True(decompiled * 2 > tried, $"Only {decompiled} of {tried} sampled stages decompiled");
    }
}
