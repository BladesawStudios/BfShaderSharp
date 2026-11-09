using System.Collections.Concurrent;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Prepared;
using BfShaderSharp.Sharc;

namespace BfShaderSharp.Preparation;

/// <summary>Turns one program of a SHARC file into a prepared model: every option combination is a program of the model.</summary>
/// <remarks>
/// The samplers, uniform blocks and attributes are the union of what the program's stages bind, in the order they are first
/// seen. A combination's bindings give the slot each stage binds a sampler or block to, with the pixel stage in the fragment
/// slot and the compute stage in the vertex slot, since a compute program has no vertex stage. Prepared files have no
/// place for task and mesh stages, so those are reported as failed stages and stored as absent.
/// </remarks>
internal sealed class SharcProgramPreparer(SharcbFile file, StageCompiler stages, int parallelism)
{
    internal ConcurrentBag<StageFailure> Failures { get; } = [];

    internal int StageCount;

    internal PreparedModelData Prepare(SharcProgram program)
    {
        SharcKeys keys = new(program);
        Dictionary<string, int> samplers = Index(program, v => v.Samplers.Select(s => s.Name));
        Dictionary<string, int> blocks = Index(program, v => v.Blocks.Select(b => b.Name));
        int count = program.VariationCount;
        int[][] variations = new int[count][];
        PreparedProgramData[] programs = new PreparedProgramData[count];
        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, combination =>
        {
            variations[combination] = Stages(program, combination, out List<SharcVariation> compiled);
            programs[combination] = new PreparedProgramData(combination, Bindings(compiled, samplers, blocks));
        });

        return new PreparedModelData(program.Name, keys.Options, keys.Rows(), keys.RowWords, [.. samplers.Select(s => new PreparedSampler(s.Key, "", s.Value))],
            Attributes(program), Blocks(program, blocks), variations, programs);
    }

    private Dictionary<string, int> Index(SharcProgram program, Func<SharcVariation, IEnumerable<string>> names)
    {
        Dictionary<string, int> index = [];
        foreach (SharcStage stage in program.Stages)
        {
            for (int v = 0; v < program.VariationCount; v++)
            {
                foreach (string name in names(file.Variation(program, v, stage))) index.TryAdd(name, index.Count);
            }
        }

        return index;
    }

    private int[] Stages(SharcProgram program, int combination, out List<SharcVariation> compiled)
    {
        int[] slots = Enumerable.Repeat(BlobPool.None, Enum.GetValues<ShaderStage>().Length).ToArray();
        compiled = [];
        foreach (SharcStage kind in program.Stages)
        {
            SharcVariation variation = file.Variation(program, combination, kind);
            Interlocked.Increment(ref StageCount);
            if (SlotOf(kind) is not { } slot)
            {
                Failures.Add(new StageFailure(program.Name, combination, kind.ToString(), "Prepared files have no place for this stage kind"));
                continue;
            }

            compiled.Add(variation);
            var (blob, error) = stages.Compile(variation.ToStageCode());
            slots[(int)slot] = blob;
            if (error is not null) Failures.Add(new StageFailure(program.Name, combination, kind.ToString(), error));
        }

        return slots;
    }

    private static ShaderStage? SlotOf(SharcStage kind) => kind switch
    {
        SharcStage.Vertex => ShaderStage.Vertex,
        SharcStage.Pixel => ShaderStage.Fragment,
        SharcStage.Geometry => ShaderStage.Geometry,
        SharcStage.Compute => ShaderStage.Compute,
        _ => null,
    };

    private static List<PreparedBinding> Bindings(List<SharcVariation> compiled, Dictionary<string, int> samplers, Dictionary<string, int> blocks)
    {
        PreparedBinding[] bindings = new PreparedBinding[samplers.Count + blocks.Count];
        Array.Fill(bindings, new PreparedBinding(-1, -1));
        foreach (SharcVariation variation in compiled)
        {
            bool fragment = variation.Stage == SharcStage.Pixel;
            foreach (SharcSymbol s in variation.Samplers) bindings[samplers[s.Name]] = With(bindings[samplers[s.Name]], fragment, s.Location);
            foreach (SharcBlock b in variation.Blocks) bindings[samplers.Count + blocks[b.Name]] = With(bindings[samplers.Count + blocks[b.Name]], fragment, b.Location);
        }

        return [.. bindings];
    }

    private static PreparedBinding With(PreparedBinding binding, bool fragment, int location) =>
        fragment ? binding with { Fragment = (short)location } : binding with { Vertex = (short)location };

    private List<PreparedAttribute> Attributes(SharcProgram program)
    {
        Dictionary<string, PreparedAttribute> attributes = [];
        if (!program.Stages.Contains(SharcStage.Vertex)) return [];
        for (int v = 0; v < program.VariationCount; v++)
        {
            foreach (SharcSymbol a in file.Variation(program, v, SharcStage.Vertex).Attributes) attributes.TryAdd(a.Name, new PreparedAttribute(a.Name, attributes.Count, a.Location));
        }

        return [.. attributes.Values];
    }

    private List<PreparedBlock> Blocks(SharcProgram program, Dictionary<string, int> blocks)
    {
        Dictionary<string, int> sizes = [];
        foreach (SharcStage stage in program.Stages)
        {
            for (int v = 0; v < program.VariationCount; v++)
            {
                foreach (SharcBlock b in file.Variation(program, v, stage).Blocks) sizes[b.Name] = Math.Max(sizes.GetValueOrDefault(b.Name), b.Size);
            }
        }

        return [.. blocks.Select(b => new PreparedBlock(b.Key, b.Value, 0, sizes[b.Key], [], BlobPool.None))];
    }
}
