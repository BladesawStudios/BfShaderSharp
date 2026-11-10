using BfShaderSharp.Bnsh;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Sharc;

/// <summary>One compiled stage of a SHARC program for one set of option values.</summary>
/// <remarks>The byte code and control code are slices of the file bytes. The symbols are the stage's bindings.</remarks>
public sealed record SharcVariation(
    SharcStage Stage,
    ReadOnlyMemory<byte> ByteCode,
    ReadOnlyMemory<byte> ControlCode,
    IReadOnlyList<SharcBlock> Blocks,
    IReadOnlyList<SharcSymbol> Buffers,
    IReadOnlyList<SharcSymbol> Attributes,
    IReadOnlyList<SharcSymbol> Uniforms,
    IReadOnlyList<SharcSymbol> Samplers)
{
    /// <summary>The stage code in the form the decompiler takes; every stage a SHARC file holds has one.</summary>
    public ShaderStageCode ToStageCode() => new(Stage switch
    {
        SharcStage.Vertex => ShaderStage.Vertex,
        SharcStage.Pixel => ShaderStage.Fragment,
        SharcStage.Geometry => ShaderStage.Geometry,
        SharcStage.Compute => ShaderStage.Compute,
        SharcStage.Task => ShaderStage.Task,
        SharcStage.Mesh => ShaderStage.Mesh,
        _ => throw new BfShaderUnsupportedException($"Stage kind {(int)Stage} is not decompiled"),
    }, ByteCode, ControlCode);
}
