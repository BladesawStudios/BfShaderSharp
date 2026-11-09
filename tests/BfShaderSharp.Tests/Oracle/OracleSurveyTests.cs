using BfShaderSharp.Bfsha;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Tests.Corpus;
using Xunit.Abstractions;

namespace BfShaderSharp.Tests.Oracle;

public class OracleSurveyTests(ITestOutputHelper output)
{
    private const int HeaderSize = 0x80;

    [Fact]
    public void NativeLoweringAcceptsTheCorpus()
    {
        if (!AmpereLowerOracle.Available || GameFiles.Folders.Count == 0) return;
        Dictionary<string, int> failures = [];
        int ok = 0, total = 0;
        foreach (string folder in GameFiles.Folders)
        foreach ((_, byte[] data) in GameFiles.BfshaFiles(folder))
        foreach (ShaderModel model in BfshaFile.Read(data).Models())
        {
            BnshFile bnsh = model.Bnsh;
            for (int i = 0; i < bnsh.VariationCount; i += Math.Max(1, GameFiles.Stride))
            foreach (ShaderStage stage in Enum.GetValues<ShaderStage>())
            {
                if (bnsh.Variation(i).Program.Code(stage) is not { } code) continue;
                total++;
                int tess = stage == ShaderStage.TessellationControl ? 1 : stage == ShaderStage.TessellationEvaluation ? 2 : 0;
                var result = AmpereLowerOracle.Lower(code.ByteCode.Span[HeaderSize..], stage == ShaderStage.Geometry, tess, 0);
                if (result.Ok) ok++;
                else failures[$"{stage}: {result.Error}"] = failures.GetValueOrDefault($"{stage}: {result.Error}") + 1;
            }
        }

        output.WriteLine($"{ok}/{total} lowered");
        foreach (var (reason, count) in failures.OrderByDescending(f => f.Value).Take(15)) output.WriteLine($"{count,6} {reason}");
    }
}
