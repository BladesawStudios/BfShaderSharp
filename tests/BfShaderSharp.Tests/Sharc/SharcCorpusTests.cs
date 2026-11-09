using BfShaderSharp.Decompile;
using BfShaderSharp.Errors;
using BfShaderSharp.Lookup;
using BfShaderSharp.Preparation;
using BfShaderSharp.Prepared;
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

    // A stage's code size is its control block's program size: the code ends exactly at the padding of self-branches the compiler appends.
    [CorpusFact]
    public void EveryStageEndsWithItsBranchPadding()
    {
        foreach (SharcbFile file in Files())
        {
            foreach (SharcVariation variation in file.Variations)
            {
                ReadOnlySpan<byte> code = variation.ByteCode.Span;
                Assert.Equal(0, code.Length % 16);
                Assert.True(code[^16..].SequenceEqual(code[^32..^16]), "The last two instructions differ");
                Assert.Equal(0x918, (code[^16] | code[^15] << 8) & 0xFFF);
            }
        }
    }

    // The first option is the most significant digit of a variation number. A vertex stage ignores the options that only the pixel code reads, so under the right order its code changes with the same few options every time.
    [CorpusFact]
    public void OptionOrderPutsTheFirstOptionMostSignificant()
    {
        SharcProgram program = Files().SelectMany(f => f.Programs).First(p => p.Name == "hdr_compose");
        SharcbFile file = Files().First(f => f.Program("hdr_compose") is not null);
        HashSet<string> vertexOptions = [];
        foreach (SharcMacro macro in program.Macros)
        {
            for (int value = 1; value < macro.Values.Count; value++)
            {
                byte[] baseCode = file.Variation(program, 0, SharcStage.Vertex).ByteCode.ToArray();
                var choice = new Dictionary<string, string> { [macro.Name] = macro.Values[value] };
                if (!baseCode.AsSpan().SequenceEqual(file.Variation(program, program.VariationIndex(choice), SharcStage.Vertex).ByteCode.Span)) vertexOptions.Add(macro.Name);
            }
        }

        Assert.Equal(["REVERSE_X", "USE_GAME_OVER_FILTER"], vertexOptions.Order());
    }

    [CorpusFact]
    public void PreparedLookupFindsEveryCombinationOfEveryProgram()
    {
        foreach (SharcbFile file in Files())
        {
            using MemoryStream output = new();
            SharcPreparer.Prepare(file, output, compressionQuality: 1);
            PreparedArchive archive = PreparedArchive.Read(output.ToArray());
            foreach (SharcProgram program in file.Programs)
            {
                ProgramLookup lookup = archive.Model(program.Name)!.CreateLookup();
                int[] sizes = [.. program.Macros.Select(m => m.Values.Count)];
                for (int combination = 0; combination < program.VariationCount; combination++)
                {
                    Dictionary<string, string> choices = [];
                    int rest = combination;
                    for (int m = sizes.Length - 1; m >= 0; m--)
                    {
                        choices[program.Macros[m].Name] = program.Macros[m].Values[rest % sizes[m]];
                        rest /= sizes[m];
                    }

                    Assert.Equal(combination, lookup.Find(choices));
                    Assert.Equal(combination, program.VariationIndex(choices));
                }
            }
        }
    }
}
