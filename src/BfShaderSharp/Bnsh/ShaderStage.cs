namespace BfShaderSharp.Bnsh;

/// <summary>A programmable stage of a shader program, numbered by its slot in the program binary.</summary>
/// <remarks>
/// The order is the order of the stage offsets in a program header, which is also the order of its reflection offsets except for the first three.
/// Task and mesh stages come from SHARC files only; a program binary has no slot for them.
/// </remarks>
public enum ShaderStage
{
    Vertex,
    TessellationControl,
    TessellationEvaluation,
    Geometry,
    Fragment,
    Compute,
    Task,
    Mesh,
}
