using BfShaderSharp.Binary;
using BfShaderSharp.Container;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Bnsh;

/// <summary>The names a stage reads and the binding slot of each: inputs, outputs, samplers, uniform buffers and storage buffers.</summary>
/// <remarks>
/// Each group is a <see cref="ResDict"/> of names, and the slot of the n-th name of a group is entry
/// <c>start + n</c> of one shared slot list. Nothing is read until a group or slot is asked for.
/// </remarks>
public readonly struct StageReflection
{
    private readonly ByteView _view;
    private readonly long _offset;

    internal StageReflection(ByteView bnsh, long offset)
    {
        _view = bnsh;
        _offset = offset;
        SlotCount = bnsh.I32(offset + 0x4C);
        if (SlotCount < 0) throw new BfShaderFormatException("Negative slot count", offset + 0x4C);
    }

    public int SlotCount { get; }

    public int ImageSlot(int index) => Slot(_view.I32(_offset + 0x48), index);

    public (int X, int Y, int Z) ComputeWorkGroup => (_view.I32(_offset + 0x3C), _view.I32(_offset + 0x40), _view.I32(_offset + 0x44));

    public ResDict Inputs => new(_view, (long)_view.U64(_offset));

    public ResDict Outputs => new(_view, (long)_view.U64(_offset + 8));

    public ResDict Samplers => new(_view, (long)_view.U64(_offset + 0x10));

    public ResDict UniformBuffers => new(_view, (long)_view.U64(_offset + 0x18));

    public ResDict StorageBuffers => new(_view, (long)_view.U64(_offset + 0x20));

    public int InputSlot(int index) => Slot(0, index);

    public int OutputSlot(int index) => Slot(_view.I32(_offset + 0x28), index);

    public int SamplerSlot(int index) => Slot(_view.I32(_offset + 0x2C), index);

    public int UniformBufferSlot(int index) => Slot(_view.I32(_offset + 0x30), index);

    public int StorageBufferSlot(int index) => Slot(_view.I32(_offset + 0x34), index);

    private int Slot(int start, int index)
    {
        int at = start + index;
        if ((uint)at >= (uint)SlotCount) return -1;
        return _view.I32(_view.I32(_offset + 0x38) + (long)at * 4);
    }
}
