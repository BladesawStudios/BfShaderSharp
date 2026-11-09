using BfShaderSharp.Prepared;
using BfShaderSharp.Sharc;

namespace BfShaderSharp.Preparation;

/// <summary>Prepares every program of a SHARC file into one prepared file.</summary>
/// <remarks>
/// Each program becomes a model of the prepared file, named for the program, and each combination of its options one
/// of the model's programs, so the lookup that serves archives serves these. Identical stages are translated once across
/// the file. The progress callback is called with the program name before each program; the library writes nowhere else.
/// </remarks>
public static class SharcPreparer
{
    public static PreparationResult Prepare(SharcbFile file, Stream output, int compressionQuality = 5, int parallelism = 0, IProgress<string>? progress = null)
    {
        BlobPool blobs = new(compressionQuality);
        StageCompiler stages = new(blobs);
        SharcProgramPreparer preparer = new(file, stages, parallelism > 0 ? parallelism : Environment.ProcessorCount);
        List<PreparedModelData> models = [];
        foreach (SharcProgram program in file.Programs)
        {
            progress?.Report(program.Name);
            models.Add(preparer.Prepare(program));
        }

        PreparedWriter.Write(output, models, blobs);
        return new PreparationResult(models.Count, models.Sum(m => m.Programs.Count), preparer.StageCount, stages.Decompiled, [.. preparer.Failures]);
    }
}
