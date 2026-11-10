using BfShaderSharp.Binary;

namespace BfShaderSharp.Bnsh;

/// <summary>The compiled stages and reflection of one shader variation.</summary>
/// <remarks>
/// Created on demand from a <see cref="BnshVariation"/>, because a file holds thousands of them and a caller
/// usually wants a few. A stage the program does not have is absent, not empty.
/// </remarks>
public readonly struct ShaderProgramBinary
{
    private static readonly ShaderStage[] ReflectionOrder =
        [ShaderStage.Vertex, ShaderStage.TessellationControl, ShaderStage.TessellationEvaluation, ShaderStage.Geometry, ShaderStage.Fragment, ShaderStage.Compute];

    private readonly ByteView _bnsh;
    private readonly long _offset;

    internal ShaderProgramBinary(ByteView bnsh, long offset)
    {
        _bnsh = bnsh;
        _offset = offset;
    }

    public bool Has(ShaderStage stage) => StageOffset(stage) != 0;

    public ShaderStageCode? Code(ShaderStage stage) =>
        StageOffset(stage) is var at and not 0 ? ShaderStageCode.Read(_bnsh, stage, at) : null;

    public StageReflection? Reflection(ShaderStage stage)
    {
        ulong table = _bnsh.U64(_offset + 0x78);
        if (table == 0 || stage > ShaderStage.Compute) return null;
        ulong at = _bnsh.U64((long)table + (long)Array.IndexOf(ReflectionOrder, stage) * 8);
        return at == 0 ? null : new StageReflection(_bnsh, (long)at);
    }

    // Stage offsets sit at 0x08 in the order vertex, tessellation control, tessellation evaluation, geometry, fragment, compute.
    private long StageOffset(ShaderStage stage) => stage > ShaderStage.Compute ? 0 : (long)_bnsh.U64(_offset + 8 + (long)stage * 8);
}
