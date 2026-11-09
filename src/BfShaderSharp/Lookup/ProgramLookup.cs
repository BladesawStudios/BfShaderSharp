using System.Buffers.Binary;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Lookup;

/// <summary>Finds the program of a shading model that was compiled for a set of option choices.</summary>
/// <remarks>
/// Built once per model by hashing the key row of every program, so an exact lookup is one hash. Options that are
/// not named take their default choice. When no program has exactly that row, the first program whose choices match
/// every named option is returned, which is how a material that names only some options is resolved. An option name
/// the model does not have is ignored; a choice the option does not have finds nothing. Rows are slices of the
/// source bytes and are never copied.
/// </remarks>
public sealed class ProgramLookup
{
    private readonly ReadOnlyMemory<byte> _keys;
    private readonly int _rowBytes;
    private readonly int _programCount;
    private readonly Dictionary<RowKey, int> _rows;
    private readonly LookupOption[] _options;
    private readonly Dictionary<string, int> _optionByName;
    private readonly Dictionary<string, int>[] _choiceByName;
    private readonly uint[] _defaultRow;

    public ProgramLookup(IReadOnlyList<LookupOption> options, ReadOnlyMemory<byte> keyRows, int rowWords, int programCount)
    {
        _keys = keyRows;
        _rowBytes = rowWords * 4;
        _programCount = programCount;
        if (_keys.Length != (long)programCount * _rowBytes) throw new BfShaderFormatException("Key table does not hold one row for each program");
        _options = [.. options];
        _optionByName = new Dictionary<string, int>(_options.Length);
        _choiceByName = new Dictionary<string, int>[_options.Length];
        _defaultRow = new uint[rowWords];
        for (int i = 0; i < _options.Length; i++)
        {
            LookupOption option = _options[i];
            if (option.Word >= rowWords) throw new BfShaderFormatException($"Option {option.Name} keys word {option.Word} of a {rowWords}-word row");
            _optionByName.TryAdd(option.Name, i);
            _choiceByName[i] = new Dictionary<string, int>(option.Choices.Count);
            for (int c = 0; c < option.Choices.Count; c++) _choiceByName[i].TryAdd(option.Choices[c], c);
            _defaultRow[option.Word] = option.WithChoice(_defaultRow[option.Word], option.DefaultChoice);
        }

        _rows = new Dictionary<RowKey, int>(programCount);
        for (int program = 0; program < programCount; program++) _rows.TryAdd(new RowKey(_keys.Slice(program * _rowBytes, _rowBytes)), program);
    }

    public int ProgramCount => _programCount;

    public int Find(IReadOnlyDictionary<string, string> choices)
    {
        uint[] row = (uint[])_defaultRow.Clone();
        List<(int Option, int Choice)> named = [];
        foreach (var (name, value) in choices)
        {
            if (!_optionByName.TryGetValue(name, out int at)) continue;
            if (!_choiceByName[at].TryGetValue(value, out int choice)) return -1;
            row[_options[at].Word] = _options[at].WithChoice(row[_options[at].Word], choice);
            named.Add((at, choice));
        }

        byte[] bytes = new byte[_rowBytes];
        for (int i = 0; i < row.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), row[i]);
        return _rows.TryGetValue(new RowKey(bytes), out int exact) ? exact : FirstMatching(named);
    }

    public Dictionary<string, string> ChoicesOf(int program)
    {
        if ((uint)program >= (uint)_programCount) throw new ArgumentOutOfRangeException(nameof(program));
        ReadOnlySpan<byte> row = Row(program);
        Dictionary<string, string> choices = new(_options.Length);
        foreach (LookupOption option in _options)
        {
            int choice = option.ChoiceOf(BinaryPrimitives.ReadUInt32LittleEndian(row[(option.Word * 4)..]));
            if (choice >= option.Choices.Count) throw new BfShaderFormatException($"Program {program} has choice {choice} of option {option.Name}, which has {option.Choices.Count}");
            choices[option.Name] = option.Choices[choice];
        }
        return choices;
    }

    private int FirstMatching(List<(int Option, int Choice)> named)
    {
        for (int program = 0; program < _programCount; program++)
        {
            ReadOnlySpan<byte> row = Row(program);
            bool all = true;
            foreach (var (option, choice) in named)
            {
                if (_options[option].ChoiceOf(BinaryPrimitives.ReadUInt32LittleEndian(row[(_options[option].Word * 4)..])) == choice) continue;
                all = false;
                break;
            }

            if (all) return program;
        }

        return -1;
    }

    private ReadOnlySpan<byte> Row(int program) => _keys.Span.Slice(program * _rowBytes, _rowBytes);

    private readonly struct RowKey(ReadOnlyMemory<byte> bytes) : IEquatable<RowKey>
    {
        private readonly ReadOnlyMemory<byte> _bytes = bytes;

        public bool Equals(RowKey other) => _bytes.Span.SequenceEqual(other._bytes.Span);

        public override bool Equals(object? obj) => obj is RowKey other && Equals(other);

        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.AddBytes(_bytes.Span);
            return hash.ToHashCode();
        }
    }
}
