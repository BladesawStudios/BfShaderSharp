namespace BfShaderSharp.Sharc;

/// <summary>The kind of a stage stored in a SHARC file, which is also the bit of the program kind that says the program has it.</summary>
/// <remarks>
/// Task and mesh are named from what the data shows: the stages occur only in programs with a pixel stage and no vertex
/// stage, have no vertex attributes, and carry the same program header as vertex stages. Compute programs hold one compute stage.
/// </remarks>
public enum SharcStage
{
    Vertex = 0,
    Pixel = 1,
    Geometry = 2,
    Compute = 3,
    Task = 4,
    Mesh = 5,
}
