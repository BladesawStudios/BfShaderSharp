namespace BfShaderSharp.Sharc;

/// <summary>An option of a SHARC program: its name and the values it can take.</summary>
/// <remarks>The first value is the default.</remarks>
public sealed record SharcMacro(string Name, IReadOnlyList<string> Values);
