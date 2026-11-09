using System.Text.Json;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Bnsh;

namespace BfShaderSharp.Preparation;

/// <summary>Exports the programs of shading models to an <see cref="IExportSink"/>: one JSON report per program and one GLSL file per stage.</summary>
/// <remarks>
/// Files are named model/program/report.json and model/program/stage.glsl, and the JSON holds the GLSL file name in
/// place of the text. A program whose report exists is skipped, so an interrupted run resumes. Programs are processed
/// in parallel. Nothing here knows a game, a renderer or a file layout beyond the sink.
/// </remarks>
public sealed class ModelExporter(IExportSink sink, int parallelism = 0)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public int Export(ShaderModel model, Func<int, bool>? includeProgram = null)
    {
        BnshFile bnsh = model.Bnsh;
        ProgramLookup lookup = model.CreateLookup();
        int exported = 0;
        ParallelOptions options = new() { MaxDegreeOfParallelism = parallelism > 0 ? parallelism : Environment.ProcessorCount };
        Parallel.For(0, model.ProgramCount, options, program =>
        {
            if (includeProgram is not null && !includeProgram(program)) return;
            string folder = $"{model.Name}/{program}";
            if (sink.Exists($"{folder}/report.json")) return;
            ProgramReport report = ProgramExporter.Build(model, bnsh, lookup, program);
            foreach (StageReport stage in report.Stages)
                if (stage.Glsl is not null) sink.WriteText($"{folder}/{stage.Stage}.glsl", stage.Glsl);
            ProgramReport slim = report with { Stages = [.. report.Stages.Select(s => s with { Glsl = s.Glsl is null ? null : $"{s.Stage}.glsl" })] };
            sink.WriteText($"{folder}/report.json", JsonSerializer.Serialize(slim, Json));
            Interlocked.Increment(ref exported);
        });
        return exported;
    }

    public int Export(BfshaFile archive, Func<int, bool>? includeProgram = null) => archive.Models().Sum(model => Export(model, includeProgram));
}
