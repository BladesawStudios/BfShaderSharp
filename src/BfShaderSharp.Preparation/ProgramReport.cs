namespace BfShaderSharp.Preparation;

/// <summary>A binding of a name to the slot it has in a stage.</summary>
public sealed record SlotReport(string Name, int Slot);

/// <summary>What one compiled stage of a program reads and the GLSL it decompiles to.</summary>
public sealed record StageReport(string Stage, string? Glsl, string? Error, IReadOnlyList<SlotReport> Inputs, IReadOnlyList<SlotReport> Outputs,
    IReadOnlyList<SlotReport> Samplers, IReadOnlyList<SlotReport> UniformBuffers, IReadOnlyList<SlotReport> StorageBuffers);

/// <summary>Where a sampler or uniform block of the model binds in a program, by stage.</summary>
public sealed record BindingReport(string Name, int Vertex, int Fragment);

/// <summary>Everything exported for one program: the options that select it, its bindings and its stages.</summary>
public sealed record ProgramReport(string Model, int Program, int Variation, IReadOnlyDictionary<string, string> Options,
    IReadOnlyList<BindingReport> Samplers, IReadOnlyList<BindingReport> UniformBlocks, IReadOnlyList<StageReport> Stages);
