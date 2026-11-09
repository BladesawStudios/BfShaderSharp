namespace BfShaderSharp.Lookup;

/// <summary>An option as the program lookup sees it: a name, the bit field of a key word that holds its choice, and the names of its choices.</summary>
/// <remarks>
/// The choice of a program is <c>(word &amp; Mask) >> Shift</c> of the key word at <see cref="Word"/>, counted in 32-bit
/// words from the start of the row. Archives and prepared files both describe their options this way, so one lookup
/// serves both.
/// </remarks>
public sealed record LookupOption(string Name, int Word, uint Mask, int Shift, int DefaultChoice, IReadOnlyList<string> Choices)
{
    public int ChoiceOf(uint keyWord) => (int)((keyWord & Mask) >> Shift);

    public uint WithChoice(uint keyWord, int choice) => (keyWord & ~Mask) | ((uint)choice << Shift);
}
