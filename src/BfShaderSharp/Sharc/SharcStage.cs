namespace BfShaderSharp.Sharc;

/// <summary>The kind of a stage stored in a SHARC file, which is also the bit of the program kind that says the program has it.</summary>
/// <remarks>
/// Stages four and five occur only in programs that also have a pixel stage, so they are probably the task and mesh
/// stages, but nothing confirms it and they are not named. Compute programs hold one compute stage.
/// </remarks>
public enum SharcStage
{
    Vertex = 0,
    Pixel = 1,
    Geometry = 2,
    Compute = 3,
    Four = 4,
    Five = 5,
}
