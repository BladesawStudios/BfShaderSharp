using BfShaderSharp.Binary;

namespace BfShaderSharp.Prepared;

/// <summary>A program of a prepared model: the variation it runs and where it binds samplers and uniform blocks.</summary>
/// <remarks>Bindings are read in place from the model's binding table, one entry for each sampler and then one for each uniform block.</remarks>
public readonly struct PreparedProgram
{
    private readonly ByteView _view;
    private readonly long _bindings;
    private readonly int _samplerCount;

    internal PreparedProgram(ByteView view, int variation, long bindings, int samplerCount)
    {
        _view = view;
        Variation = variation;
        _bindings = bindings;
        _samplerCount = samplerCount;
    }

    public int Variation { get; }

    public PreparedBinding Sampler(int index) => Binding(index);

    public PreparedBinding UniformBlock(int index) => Binding(_samplerCount + index);

    private PreparedBinding Binding(int slot)
    {
        long at = _bindings + (long)slot * 4;
        return new PreparedBinding((short)_view.U16(at), (short)_view.U16(at + 2));
    }
}
