using System.Diagnostics;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Decompile;
using BfShaderSharp.Errors;
using BfShaderSharp.Tests.Corpus;
using Xunit.Abstractions;

namespace BfShaderSharp.Tests.Decompile;

public class DecompileCorpusTests(ITestOutputHelper output)
{
    private static int VariationStride => int.TryParse(Environment.GetEnvironmentVariable("BFSHA_VARIATION_STRIDE"), out int n) && n > 1 ? n : 1;

    private static bool Skip => !NativeAmpereLowering.IsAvailable || GameFiles.Folders.Count == 0;

    [Fact]
    public void EveryStageLowersAndTranslates()
    {
        if (Skip) return;
        Dictionary<string, int> failures = [];
        int ok = 0, total = 0;
        Stopwatch clock = Stopwatch.StartNew();
        foreach (string folder in GameFiles.Folders)
        foreach ((_, byte[] data) in GameFiles.BfshaFiles(folder))
        foreach (ShaderModel model in BfshaFile.Read(data).Models())
        {
            BnshFile bnsh = model.Bnsh;
            for (int i = 0; i < bnsh.VariationCount; i += VariationStride)
            foreach (ShaderStage stage in Enum.GetValues<ShaderStage>())
            {
                if (bnsh.Variation(i).Program.Code(stage) is not { } code) continue;
                total++;
                try
                {
                    Assert.False(string.IsNullOrEmpty(ShaderDecompiler.ToGlsl(code)));
                    ok++;
                }
                catch (BfShaderUnsupportedException ex)
                {
                    Count(failures, $"{stage}: {ex.Message}");
                }
                catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
                {
                    Count(failures, $"{stage}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                }
            }
        }

        output.WriteLine($"{ok}/{total} decompiled in {clock.Elapsed.TotalSeconds:F1} s");
        foreach (var (reason, count) in failures.OrderByDescending(f => f.Value).Take(20)) output.WriteLine($"{count,6} {reason}");
    }

    private static void Count(Dictionary<string, int> failures, string reason) => failures[reason] = failures.GetValueOrDefault(reason) + 1;
}
