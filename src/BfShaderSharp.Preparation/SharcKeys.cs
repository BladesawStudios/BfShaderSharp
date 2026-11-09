using System.Numerics;
using BfShaderSharp.Lookup;
using BfShaderSharp.Sharc;

namespace BfShaderSharp.Preparation;

/// <summary>Describes the option combinations of a SHARC program the way a prepared file's lookup expects: options as bit fields of key rows.</summary>
/// <remarks>
/// Each option takes as many bits as its values need, packed into 32-bit words without crossing a word. A combination's
/// row holds the number of each option's value, so the row of combination <c>n</c> is its mixed-radix digits. The default
/// choice of every option is its first value.
/// </remarks>
internal sealed class SharcKeys
{
    private readonly SharcProgram _program;
    private readonly (int Word, int Shift)[] _places;

    internal SharcKeys(SharcProgram program)
    {
        _program = program;
        _places = new (int, int)[program.Macros.Count];
        List<LookupOption> options = [];
        int word = 0;
        int shift = 0;
        for (int i = 0; i < program.Macros.Count; i++)
        {
            SharcMacro macro = program.Macros[i];
            int bits = macro.Values.Count <= 1 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)(macro.Values.Count - 1));
            if (shift + bits > 32)
            {
                word++;
                shift = 0;
            }

            _places[i] = (word, shift);
            uint mask = bits == 0 ? 0 : (uint)((1UL << bits) - 1) << shift;
            options.Add(new LookupOption(macro.Name, word, mask, shift, 0, macro.Values));
            shift += bits;
        }

        Options = options;
        RowWords = word + 1;
    }

    internal IReadOnlyList<LookupOption> Options { get; }

    internal int RowWords { get; }

    internal byte[] Rows()
    {
        int count = _program.VariationCount;
        byte[] rows = new byte[count * RowWords * 4];
        for (int combination = 0; combination < count; combination++)
        {
            uint[] row = new uint[RowWords];
            int rest = combination;
            for (int i = _program.Macros.Count - 1; i >= 0; i--)
            {
                int values = _program.Macros[i].Values.Count;
                row[_places[i].Word] |= (uint)(rest % values) << _places[i].Shift;
                rest /= values;
            }

            for (int w = 0; w < RowWords; w++) BitConverter.TryWriteBytes(rows.AsSpan((combination * RowWords + w) * 4), row[w]);
        }

        return rows;
    }
}
