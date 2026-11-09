using System.Security.Cryptography;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Decompile;
using BfShaderSharp.Decompile.Ampere;
using BfShaderSharp.Tests.Corpus;
using Xunit.Abstractions;

namespace BfShaderSharp.Tests.Decompile;

public class AmpereParityTests(ITestOutputHelper output)
{
    private const int ExamplesPerKind = 3;

    private static int VariationStride => int.TryParse(Environment.GetEnvironmentVariable("BFSHA_VARIATION_STRIDE"), out int n) && n > 1 ? n : 1;

    // Every distinct stage program of the corpus, once, with a label naming where it came from.
    private static IEnumerable<(string Label, ShaderStage Stage, byte[] Code)> DistinctStages()
    {
        HashSet<string> seen = [];
        foreach (string folder in GameFiles.Folders)
        foreach ((string name, byte[] data) in GameFiles.BfshaFiles(folder))
        foreach (ShaderModel model in BfshaFile.Read(data).Models())
        {
            BnshFile bnsh = model.Bnsh;
            for (int i = 0; i < bnsh.VariationCount; i += VariationStride)
            foreach (ShaderStage stage in Enum.GetValues<ShaderStage>())
            {
                if (bnsh.Variation(i).Program.Code(stage) is not { } code) continue;
                byte[] bytes = code.ByteCode.ToArray();
                if (bytes.Length <= 0x80 || !seen.Add(Convert.ToHexString(SHA1.HashData(bytes)))) continue;
                yield return ($"{name} variation {i}", stage, bytes[0x80..]);
            }
        }
    }

    private static string Describe(NativeLowering native, LoweredProgram managed)
    {
        if (native.Error is null && managed.Succeeded)
        {
            int length = Math.Min(native.Words.Length, managed.Instructions.Count), first = 0;
            while (first < length && native.Words[first] == managed.Instructions[first]) first++;
            if (native.Words.Length == managed.Instructions.Count && first == length) return "";
            string managedWord = first < managed.Instructions.Count ? $"{managed.Instructions[first]:x16}" : "-";
            string nativeWord = first < native.Words.Length ? $"{native.Words[first]:x16}" : "-";
            return $"words differ at {first} (native {nativeWord}, managed {managedWord}; lengths {native.Words.Length}/{managed.Instructions.Count})";
        }

        if (native.Error is not null && !managed.Succeeded)
            return native.Error == managed.Error && native.ErrorOffset == managed.ErrorOffset ? "" : $"errors differ: native '{native.Error}' at {native.ErrorOffset}, managed '{managed.Error}' at {managed.ErrorOffset}";
        return native.Error is null ? $"managed rejected: '{managed.Error}' at {managed.ErrorOffset}" : $"native rejected: '{native.Error}' at {native.ErrorOffset}";
    }

    [Fact]
    public void ManagedLoweringMatchesTheNativeLoweringWordForWord()
    {
        if (!NativeAmpereLowering.IsAvailable || GameFiles.Folders.Count == 0) return;
        Dictionary<string, List<string>> mismatches = [];
        int total = 0, same = 0, rejected = 0;
        foreach ((string label, ShaderStage stage, byte[] code) in DistinctStages())
        {
            total++;
            bool geometry = stage == ShaderStage.Geometry;
            TessellationStage tessellation = stage switch { ShaderStage.TessellationControl => TessellationStage.Control, ShaderStage.TessellationEvaluation => TessellationStage.Evaluation, _ => TessellationStage.None };
            NativeLowering native = NativeAmpereLowering.Lower(code, geometry, (int)tessellation);
            LoweredProgram managed;
            try
            {
                managed = AmpereLowerer.Lower(code, LoweringOptions.ForStage(code, geometry, tessellation));
            }
            catch (Exception ex)
            {
                managed = new LoweredProgram();
                managed.Fail(0, $"exception {ex.GetType().Name}: {ex.Message.Split('\n')[0]} {ex.StackTrace?.Split('\n')[0]}");
            }

            string difference = Describe(native, managed);
            if (difference.Length == 0)
            {
                same++;
                if (native.Error is not null) rejected++;
                continue;
            }

            string kind = difference.Split('(', '\'')[0].Trim();
            if (!mismatches.TryGetValue(kind, out List<string>? examples)) mismatches[kind] = examples = [];
            examples.Add($"{label} {stage}: {difference}");
        }

        output.WriteLine($"{same}/{total} programs identical, {rejected} of them rejected by both for the same reason");
        foreach ((string kind, List<string> examples) in mismatches.OrderByDescending(m => m.Value.Count))
        {
            output.WriteLine($"{examples.Count,6} {kind}");
            foreach (string example in examples.Take(ExamplesPerKind)) output.WriteLine($"         {example}");
        }

        Assert.Equal(total, same);
    }
}
