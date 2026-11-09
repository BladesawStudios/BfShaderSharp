using BfShaderSharp.Decompile.Ampere;

namespace BfShaderSharp.Tests.Decompile.Ampere;

/// <summary>Checks every opcode handler against real instructions whose lowering was recorded from the native reference.</summary>
/// <remarks>
/// Samples/instruction-samples.txt holds up to four distinct encodings of each opcode found in the game shaders, each
/// followed by the Maxwell words (or the error and its offset) the native lowering produced for that instruction
/// alone before an EXIT. The AmpereParityTests regenerate it with BFS_WRITE_SAMPLES. This is the per-handler
/// known-answer test, and it runs without the game files or the native library.
/// </remarks>
public class InstructionSampleTests
{
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "Decompile", "Ampere", "Samples", "instruction-samples.txt");

    private static string Describe(LoweredProgram program) =>
        program.Succeeded ? $"ok {string.Join(',', program.Instructions.Select(w => w.ToString("x16")))}" : $"error {program.ErrorOffset} {program.Error}";

    [Fact]
    public void EveryRecordedInstructionLowersAsTheNativeLoweringDid()
    {
        string[] lines = File.ReadAllLines(SamplePath);
        Assert.True(lines.Length > 500);
        List<string> wrong = [];
        foreach (string line in lines)
        {
            int space = line.IndexOf(' ');
            byte[] program = [.. Convert.FromHexString(line[..space]), .. Asm.Program(Asm.Exit())];
            string actual = Describe(AmpereLowerer.Lower(program, new LoweringOptions(Tensor: true)));
            if (actual != line[(space + 1)..]) wrong.Add($"{line[..space]}: expected {line[(space + 1)..]} but got {actual}");
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void TheSamplesCoverManyOpcodesAndBothOutcomes()
    {
        string[] lines = File.ReadAllLines(SamplePath);
        Assert.True(lines.Select(l => new AmpereInstruction(Convert.FromHexString(l[..l.IndexOf(' ')])).Opcode).Distinct().Count() > 300);
        Assert.Contains(lines, l => l.Contains(" ok "));
        Assert.Contains(lines, l => l.Contains(" error "));
    }
}
