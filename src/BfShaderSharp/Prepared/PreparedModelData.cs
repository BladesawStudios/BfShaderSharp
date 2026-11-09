using BfShaderSharp.Lookup;

namespace BfShaderSharp.Prepared;

/// <summary>A sampler of a prepared model: name, annotation and index.</summary>
/// <remarks>Plain data handed to the writer and returned by the reader.</remarks>
public sealed record PreparedSampler(string Name, string Annotation, int Index);

/// <summary>A vertex attribute of a prepared model and the input location it binds to; -1 when unused.</summary>
/// <remarks>Plain data handed to the writer and returned by the reader.</remarks>
public sealed record PreparedAttribute(string Name, int Index, int Location);

/// <summary>A variable inside a uniform block: its name, index and byte offset.</summary>
/// <remarks>Plain data handed to the writer and returned by the reader.</remarks>
public sealed record PreparedUniform(string Name, int Index, int Offset, int BlockIndex);

/// <summary>A uniform block of a prepared model: its kind, size, variables and the blob holding its default contents.</summary>
/// <remarks>The default buffer is a blob index, or -1 when the block has none.</remarks>
public sealed record PreparedBlock(string Name, int Index, int Type, int ByteSize, IReadOnlyList<PreparedUniform> Uniforms, int DefaultBuffer);

/// <summary>The binding of one sampler or uniform block in a program, by stage; -1 means unused.</summary>
/// <remarks>Plain data handed to the writer and returned by the reader.</remarks>
public readonly record struct PreparedBinding(short Vertex, short Fragment);

/// <summary>A program of a prepared model: the variation it runs and where it binds the model's samplers, then its uniform blocks.</summary>
/// <remarks>Bindings hold one entry for each sampler of the model followed by one for each uniform block.</remarks>
public sealed record PreparedProgramData(int Variation, IReadOnlyList<PreparedBinding> Bindings);

/// <summary>Everything the writer stores for one shading model.</summary>
/// <remarks>
/// Variations are lists of six blob indices, one for each stage in the order of <c>ShaderStage</c>, with -1 for a stage the
/// variation does not have. Key rows are the raw rows of the archive, one for each program, so the lookup is unchanged.
/// </remarks>
public sealed record PreparedModelData(
    string Name,
    IReadOnlyList<LookupOption> Options,
    ReadOnlyMemory<byte> KeyRows,
    int RowWords,
    IReadOnlyList<PreparedSampler> Samplers,
    IReadOnlyList<PreparedAttribute> Attributes,
    IReadOnlyList<PreparedBlock> Blocks,
    IReadOnlyList<int[]> Variations,
    IReadOnlyList<PreparedProgramData> Programs);
