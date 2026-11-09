namespace BfShaderSharp.Sharc;

/// <summary>A named binding of a compiled stage, such as a sampler or a vertex attribute, and the slot it is bound to.</summary>
/// <remarks>Plain data returned by the reader.</remarks>
public sealed record SharcSymbol(string Name, int Location);

/// <summary>A uniform block of a compiled stage: its name, binding slot and size in bytes.</summary>
/// <remarks>Plain data returned by the reader.</remarks>
public sealed record SharcBlock(string Name, int Location, int Size);
