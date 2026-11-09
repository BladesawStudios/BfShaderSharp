using System.Diagnostics;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Tests.Corpus;
using Xunit.Abstractions;

namespace BfShaderSharp.Tests;

public class CorpusSurveyTests(ITestOutputHelper output)
{
    [CorpusFact]
    public void EveryModelOfEveryArchiveReadsItsBinary()
    {
        foreach (string folder in GameFiles.Folders)
        foreach ((string name, byte[] data) in GameFiles.BfshaFiles(folder))
        {
            Stopwatch clock = Stopwatch.StartNew();
            BfshaFile file = BfshaFile.Read(data);
            long programs = 0, stages = 0;
            foreach (ShaderModel model in file.Models())
            {
                BnshFile bnsh = model.Bnsh;
                for (int i = 0; i < bnsh.VariationCount; i++)
                {
                    ShaderProgramBinary program = bnsh.Variation(i).Program;
                    programs++;
                    foreach (ShaderStage stage in Enum.GetValues<ShaderStage>())
                    {
                        if (program.Code(stage) is { } code) { stages++; Assert.True(code.ByteCode.Length > 0); _ = program.Reflection(stage)?.Samplers.Count; }
                    }
                }
            }

            output.WriteLine($"{name}: {file.ModelCount} models, {programs} programs, {stages} stages in {clock.ElapsedMilliseconds} ms");
        }
    }
}
