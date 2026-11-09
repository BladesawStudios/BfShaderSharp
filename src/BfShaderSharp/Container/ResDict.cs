using BfShaderSharp.Binary;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Container;

/// <summary>A name lookup table stored in a file, read in place without building any collection.</summary>
/// <remarks>
/// The table is a header and 16-byte nodes, the first being the root. Keys are numbered from zero in node order,
/// which is the order of the value array the table indexes. A name is only turned into a string when asked for,
/// and <see cref="IndexOf(ReadOnlySpan{byte})"/> compares the stored bytes, so looking a name up allocates nothing.
/// An offset of zero is an empty table.
/// </remarks>
public readonly struct ResDict
{
    private const int NodeSize = 16;
    private readonly ByteView _view;
    private readonly long _offset;

    public ResDict(ByteView view, long offset)
    {
        _view = view;
        _offset = offset;
        if (offset == 0)
        {
            Count = 0;
            return;
        }

        int count = view.I32(offset + 4);
        if (count < 0 || offset + 8 + (long)(count + 1) * NodeSize > view.Length) throw new BfShaderFormatException($"Dictionary claims {count} nodes", offset);
        Count = count;
    }

    public int Count { get; }

    public ReadOnlySpan<byte> KeyBytes(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _view.StringBytes((long)_view.U64(_offset + 8 + (long)(index + 1) * NodeSize + 8));
    }

    public string Key(int index) => System.Text.Encoding.UTF8.GetString(KeyBytes(index));

    public int IndexOf(ReadOnlySpan<byte> key)
    {
        for (int i = 0; i < Count; i++)
        {
            if (KeyBytes(i).SequenceEqual(key)) return i;
        }

        return -1;
    }

    public int IndexOf(string key) => IndexOf(System.Text.Encoding.UTF8.GetBytes(key));

    public IEnumerable<string> Keys()
    {
        for (int i = 0; i < Count; i++) yield return Key(i);
    }
}
