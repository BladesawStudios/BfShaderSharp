using BfShaderSharp.Binary;
using BfShaderSharp.Container;

namespace BfShaderSharp.Bfsha;

/// <summary>The static or dynamic options of a shading model, in the order of their names.</summary>
/// <remarks>Holds the name table and the position of the option records; an option is read when indexed.</remarks>
public readonly struct OptionTable
{
    private readonly ByteView _view;
    private readonly long _values;
    private readonly ResDict _names;

    internal OptionTable(ByteView view, long values, ResDict names)
    {
        _view = view;
        _values = values;
        _names = names;
    }

    public int Count => _names.Count;

    public ShaderOption this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return new ShaderOption(_view, _values + (long)index * ShaderOption.Size, _names.Key(index));
        }
    }

    public int IndexOf(string name) => _names.IndexOf(name);
}
