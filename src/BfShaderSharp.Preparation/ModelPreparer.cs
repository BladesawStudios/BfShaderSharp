using System.Collections.Concurrent;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Errors;
using BfShaderSharp.Prepared;

namespace BfShaderSharp.Preparation;

/// <summary>Turns one shading model of an archive into the data a prepared file stores for it.</summary>
/// <remarks>
/// Every variation that a program uses is decompiled, in parallel; variations no program uses stay empty. Samplers,
/// attributes, uniform blocks with their default buffers, option tables and key rows are copied from the archive, and
/// each program's bindings are laid out one entry for each sampler and then one for each block. Failures are
/// collected and never stop the model.
/// </remarks>
internal sealed class ModelPreparer(StageCompiler stages, BlobPool blobs, int parallelism)
{
    internal ConcurrentBag<StageFailure> Failures { get; } = [];

    internal int StageCount;

    internal PreparedModelData Prepare(ShaderModel model)
    {
        BnshFile bnsh = model.Bnsh;
        var programs = new List<PreparedProgramData>(model.ProgramCount);
        bool[] used = new bool[bnsh.VariationCount];
        for (int i = 0; i < model.ProgramCount; i++)
        {
            ShaderProgram record = model.Program(i);
            if ((uint)record.VariationIndex >= (uint)used.Length) throw new BfShaderFormatException($"Program {i} names variation {record.VariationIndex} of {used.Length}");
            used[record.VariationIndex] = true;
            programs.Add(new PreparedProgramData(record.VariationIndex, Bindings(model, record)));
        }

        int[][] variations = new int[used.Length][];
        Parallel.For(0, used.Length, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, v =>
            variations[v] = used[v] ? Stages(model.Name, v, bnsh.Variation(v)) : EmptyVariation());
        return new PreparedModelData(model.Name, model.Options(), model.KeyRows, model.RowWords, Samplers(model), Attributes(model), Blocks(model), variations, programs);
    }

    private static int[] EmptyVariation() => Enumerable.Repeat(BlobPool.None, Enum.GetValues<ShaderStage>().Length).ToArray();

    private int[] Stages(string model, int variation, BnshVariation entry)
    {
        int[] blobsOfStages = EmptyVariation();
        ShaderProgramBinary binary = entry.Program;
        foreach (ShaderStage stage in Enum.GetValues<ShaderStage>())
        {
            if (binary.Code(stage) is not { } code) continue;
            Interlocked.Increment(ref StageCount);
            var (blob, error) = stages.Compile(code);
            blobsOfStages[(int)stage] = blob;
            if (error is not null) Failures.Add(new StageFailure(model, variation, stage.ToString(), error));
        }

        return blobsOfStages;
    }

    private static List<PreparedBinding> Bindings(ShaderModel model, ShaderProgram record)
    {
        List<PreparedBinding> bindings = new(model.Samplers.Count + model.UniformBlocks.Count);
        for (int i = 0; i < model.Samplers.Count; i++)
            bindings.Add(i < record.SamplerCount ? Narrow(record.Sampler(i)) : new PreparedBinding(-1, -1));
        for (int i = 0; i < model.UniformBlocks.Count; i++)
            bindings.Add(i < record.UniformBlockCount ? Narrow(record.UniformBlock(i)) : new PreparedBinding(-1, -1));
        return bindings;
    }

    private static PreparedBinding Narrow(StageLocations locations)
    {
        if (locations.Vertex is < short.MinValue or > short.MaxValue || locations.Fragment is < short.MinValue or > short.MaxValue)
            throw new BfShaderFormatException($"Binding location {locations} does not fit 16 bits");
        return new PreparedBinding((short)locations.Vertex, (short)locations.Fragment);
    }

    private static List<PreparedSampler> Samplers(ShaderModel model) =>
        [.. model.Samplers.All().Select(s => new PreparedSampler(s.Name, s.Annotation, s.Index))];

    private static List<PreparedAttribute> Attributes(ShaderModel model) =>
        [.. model.Attributes.All().Select(a => new PreparedAttribute(a.Name, a.Index, a.Location))];

    private List<PreparedBlock> Blocks(ShaderModel model) =>
        [.. model.UniformBlocks.All().Select(block => new PreparedBlock(block.Name, block.Index, (int)block.Type, block.ByteSize,
            [.. block.Uniforms.All().Select(u => new PreparedUniform(u.Name, u.Index, u.Offset, u.BlockIndex))],
            block.DefaultBuffer.IsEmpty ? BlobPool.None : blobs.Add(block.DefaultBuffer.Span)))];
}
