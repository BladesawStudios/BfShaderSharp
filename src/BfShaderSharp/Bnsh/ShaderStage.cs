namespace BfShaderSharp.Bnsh;

/// <summary>A programmable stage of a shader program, numbered by its slot in the program binary.</summary>
/// <remarks>The order is the order of the stage offsets in a program header, which is also the order of its reflection offsets except for the first three.</remarks>
public enum ShaderStage
{
    Vertex,
    TessellationControl,
    TessellationEvaluation,
    Geometry,
    Fragment,
    Compute,
}
