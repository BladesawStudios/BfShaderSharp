namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Which tessellation stage a program belongs to, if any.</summary>
/// <remarks>The lowering handles the patch-related system values of the two tessellation stages differently.</remarks>
public enum TessellationStage
{
    None,
    Control,
    Evaluation,
}

/// <summary>What the lowering needs to know about the program besides its instructions.</summary>
/// <remarks>
/// <see cref="Geometry"/> marks a geometry stage, whose exit handler is recognized and replaced. <see cref="Tensor"/>
/// selects the compact matrix forms. <see cref="NativeCodeAddress"/> is the address the code is loaded at, which
/// programs that read their own program counter need. <see cref="VerifiedTraps"/> lists the byte offsets of trap
/// instructions known to be unreachable.
/// </remarks>
public sealed record LoweringOptions(
    bool Geometry = false,
    bool Tensor = false,
    ulong NativeCodeAddress = 0,
    IReadOnlyCollection<uint>? VerifiedTraps = null,
    TessellationStage Tessellation = TessellationStage.None);
