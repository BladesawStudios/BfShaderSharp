using BfShaderSharp.Binary;
using BfShaderSharp.Container;

namespace BfShaderSharp.Bfsha;

/// <summary>A compile-time switch of a shading model and the bit field of a program key that stores its choice.</summary>
/// <remarks>
/// A program's key is a row of 32-bit words. The choice of this option is <c>(word &amp; Mask) >> Shift</c> of the
/// word at <see cref="KeyIndex"/>, counted from the start of the static or dynamic half of the row, and it indexes
/// <see cref="Choices"/>. Reads are done when a member is asked for.
/// </remarks>
public readonly struct ShaderOption
{
    internal const int Size = 0x28;
    private readonly ByteView _view;
    private readonly long _at;
    private readonly OptionLayout _layout;

    internal ShaderOption(ByteView view, long at, string name, OptionLayout layout)
    {
        _view = view;
        _at = at;
        Name = name;
        _layout = layout;
    }

    public string Name { get; }

    public ResDict Choices => new(_view, (long)_view.U64(_at + 8));

    public int DefaultChoice => _layout.WideCounts ? (short)_view.U16(_at + _layout.DefaultChoice) : _view.U8(_at + _layout.DefaultChoice);

    public byte KeyOffset => _view.U8(_at + _layout.KeyOffset);

    public uint Mask => _view.U32(_at + 0x20);

    public byte KeyIndex => _view.U8(_at + _layout.KeyIndex);

    public byte Shift => _view.U8(_at + _layout.Shift);

    public int ChoiceOf(uint keyWord) => (int)((keyWord & Mask) >> Shift);

    public uint WithChoice(uint keyWord, int choice) => (keyWord & ~Mask) | ((uint)choice << Shift);
}
