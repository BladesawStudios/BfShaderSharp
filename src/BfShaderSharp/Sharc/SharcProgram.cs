using System.Numerics;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Sharc;

/// <summary>A program of a SHARC file: a name, the options it varies over and where its compiled stages start.</summary>
/// <remarks>
/// A program has one compiled stage for each stage its kind names, for every combination of its options. The
/// combinations are numbered with the first option as the most significant digit, and the stages of one combination
/// follow each other from the base index in the order of <see cref="SharcStage"/>. An option that is not named takes
/// its first value.
/// </remarks>
public sealed record SharcProgram(string Name, int Kind, int BaseIndex, IReadOnlyList<SharcMacro> Macros)
{
    public int StageCount => BitOperations.PopCount((uint)Kind);

    public IReadOnlyList<SharcStage> Stages => [.. Enumerable.Range(0, 6).Where(bit => (Kind >> bit & 1) != 0).Select(bit => (SharcStage)bit)];

    public int VariationCount => Macros.Aggregate(1, (count, macro) => count * macro.Values.Count);

    public int VariationIndex(IReadOnlyDictionary<string, string> choices)
    {
        int index = 0;
        foreach (SharcMacro macro in Macros)
        {
            int value = 0;
            if (choices.TryGetValue(macro.Name, out string? name))
            {
                value = macro.Values.ToList().IndexOf(name);
                if (value < 0) throw new ArgumentException($"{name} is not a value of option {macro.Name}", nameof(choices));
            }

            index = index * macro.Values.Count + value;
        }

        return index;
    }

    public int BinaryIndex(int variation, SharcStage stage)
    {
        int rank = Stages.ToList().IndexOf(stage);
        if (rank < 0) throw new BfShaderFormatException($"Program {Name} has no {stage} stage");
        if ((uint)variation >= (uint)VariationCount) throw new ArgumentOutOfRangeException(nameof(variation));
        return BaseIndex + variation * StageCount + rank;
    }
}
