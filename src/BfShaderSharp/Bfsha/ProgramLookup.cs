using System.Buffers.Binary;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Bfsha;

/// <summary>Finds the program of a shading model that was compiled for a set of option choices.</summary>
/// <remarks>
/// Built once per model by hashing the key row of every program, so an exact lookup is one hash. Options that are
/// not named take their default choice. When no program has exactly that row, the first program whose choices match
/// every named option is returned, which is how a material that names only some options is resolved. An option name
/// the model does not have is ignored; a choice the option does not have finds nothing.
/// </remarks>
public sealed class ProgramLookup
{
    private readonly ShaderModel _model;
    private readonly ReadOnlyMemory<byte> _keys;
    private readonly int _rowWords;
    private readonly Dictionary<RowKey, int> _rows;
    private readonly Slot[] _slots;
    private readonly Dictionary<string, int> _slotByName;
    private readonly uint[] _defaultRow;

    internal ProgramLookup(ShaderModel model)
    {
        _model = model;
        _keys = model.KeyTable;
        _rowWords = model.StaticKeyLength + model.DynamicKeyLength;
        int count = model.ProgramCount;
        if (_keys.Length != (long)count * _rowWords * 4) throw new BfShaderFormatException("Key table does not hold one row for each program");
        _rows = new Dictionary<RowKey, int>(count);
        for (int i = 0; i < count; i++) _rows.TryAdd(new RowKey(_keys.Slice(i * _rowWords * 4, _rowWords * 4)), i);
        List<Slot> slots = [];
        AddSlots(slots, model.StaticOptions, isStatic: true);
        AddSlots(slots, model.DynamicOptions, isStatic: false);
        _slots = [.. slots];
        _slotByName = new Dictionary<string, int>(_slots.Length);
        for (int i = 0; i < _slots.Length; i++) _slotByName.TryAdd(_slots[i].Option.Name, i);
        _defaultRow = new uint[_rowWords];
        foreach (Slot slot in _slots) _defaultRow[slot.Word] = slot.Option.WithChoice(_defaultRow[slot.Word], slot.Option.DefaultChoice);
    }

    public int Find(IReadOnlyDictionary<string, string> options)
    {
        uint[] row = (uint[])_defaultRow.Clone();
        List<(Slot Slot, int Choice)> named = [];
        foreach (var (name, value) in options)
        {
            if (!_slotByName.TryGetValue(name, out int at)) continue;
            Slot slot = _slots[at];
            if (!slot.ChoiceByName.TryGetValue(value, out int choice)) return -1;
            row[slot.Word] = slot.Option.WithChoice(row[slot.Word], choice);
            named.Add((slot, choice));
        }

        byte[] bytes = new byte[_rowWords * 4];
        for (int i = 0; i < row.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), row[i]);
        return _rows.TryGetValue(new RowKey(bytes), out int exact) ? exact : FirstMatching(named);
    }

    /// <summary>The name of the choice each option has in a program, by option name.</summary>
    public Dictionary<string, string> ChoicesOf(int program)
    {
        ReadOnlySpan<byte> row = Row(program);
        Dictionary<string, string> choices = new(_slots.Length);
        foreach (Slot slot in _slots)
            choices[slot.Option.Name] = slot.ChoiceNames[slot.Option.ChoiceOf(BinaryPrimitives.ReadUInt32LittleEndian(row[(slot.Word * 4)..]))];
        return choices;
    }

    private int FirstMatching(List<(Slot Slot, int Choice)> named)
    {
        for (int program = 0; program < _model.ProgramCount; program++)
        {
            ReadOnlySpan<byte> row = Row(program);
            bool all = true;
            foreach (var (slot, choice) in named)
            {
                if (slot.Option.ChoiceOf(BinaryPrimitives.ReadUInt32LittleEndian(row[(slot.Word * 4)..])) == choice) continue;
                all = false;
                break;
            }

            if (all) return program;
        }

        return -1;
    }

    private ReadOnlySpan<byte> Row(int program) => _keys.Span.Slice(program * _rowWords * 4, _rowWords * 4);

    // A static option's word is its key index; a dynamic option's follows the static words, less the key offset.
    private void AddSlots(List<Slot> slots, NamedTable<ShaderOption> table, bool isStatic)
    {
        for (int i = 0; i < table.Count; i++)
        {
            ShaderOption option = table[i];
            int word = isStatic ? option.KeyIndex : _model.StaticKeyLength + option.KeyIndex - option.KeyOffset;
            string[] names = [.. option.Choices.Keys()];
            Dictionary<string, int> byName = new(names.Length);
            for (int c = 0; c < names.Length; c++) byName.TryAdd(names[c], c);
            slots.Add(new Slot(option, word, names, byName));
        }
    }

    private sealed record Slot(ShaderOption Option, int Word, string[] ChoiceNames, Dictionary<string, int> ChoiceByName);

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
