using System.Text;
using BfShaderSharp.Binary;

namespace BfShaderSharp.Prepared;

/// <summary>Assigns an index to each distinct string of a prepared file and writes the string table.</summary>
/// <remarks>The table is a count, then offsets of every string and one past the last, then the UTF-8 bytes. The empty string is index 0.</remarks>
internal sealed class StringPool
{
    private readonly Dictionary<string, int> _index = [];
    private readonly List<string> _strings = [];

    internal StringPool() => Intern("");

    internal int Intern(string value)
    {
        if (_index.TryGetValue(value, out int existing)) return existing;
        _strings.Add(value);
        return _index[value] = _strings.Count - 1;
    }

    internal void Write(ByteSink sink)
    {
        sink.U32((uint)_strings.Count);
        byte[][] encoded = [.. _strings.Select(Encoding.UTF8.GetBytes)];
        uint offset = 0;
        foreach (byte[] bytes in encoded)
        {
            sink.U32(offset);
            offset += (uint)bytes.Length;
        }

        sink.U32(offset);
        foreach (byte[] bytes in encoded) sink.Bytes(bytes);
    }
}
