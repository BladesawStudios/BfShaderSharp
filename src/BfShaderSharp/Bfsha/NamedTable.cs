using BfShaderSharp.Binary;
using BfShaderSharp.Container;

namespace BfShaderSharp.Bfsha;

/// <summary>A table of fixed-size records that a name dictionary indexes: options, samplers, attributes or uniform blocks.</summary>
/// <remarks>Holds the position of the records and the names; a record is read when indexed, with its name from the dictionary.</remarks>
public readonly struct NamedTable<T>
{
    private readonly ByteView _view;
    private readonly long _values;
    private readonly int _stride;
    private readonly ResDict _names;
    private readonly Func<ByteView, long, string, T> _make;

    internal NamedTable(ByteView view, long values, int stride, ResDict names, Func<ByteView, long, string, T> make)
    {
        _view = view;
        _values = values;
        _stride = stride;
        _names = names;
        _make = make;
    }

    public int Count => _names.Count;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _make(_view, _values + (long)index * _stride, _names.Key(index));
        }
    }

    public string Name(int index) => _names.Key(index);

    public int IndexOf(string name) => _names.IndexOf(name);

    public IEnumerable<T> All()
    {
        for (int i = 0; i < Count; i++) yield return this[i];
    }
}
