using BfShaderSharp.Bfsha;
using BfShaderSharp.Prepared;

namespace BfShaderSharp.Preparation;

/// <summary>Prepares every shading model of a BFSHA archive into one prepared file.</summary>
/// <remarks>
/// Models are prepared one after another and the variations of each in parallel, with identical stages translated
/// once across the whole archive. When wanted names programs for each model, only the variations those programs use are decompiled and the rest stay empty, which is how an archive is prepared for the programs some materials use; a model it does not list has none. The result lists the stages that could not be decompiled. The progress callback, when
/// given, is called with the model name before each model starts; the library writes nowhere else.
/// </remarks>
public static class ArchivePreparer
{
    public static PreparationResult Prepare(BfshaFile archive, Stream output, int compressionQuality = 5, int parallelism = 0, IProgress<string>? progress = null, IReadOnlyDictionary<string, IReadOnlySet<int>>? wanted = null)
    {
        BlobPool blobs = new(compressionQuality);
        StageCompiler stages = new(blobs);
        ModelPreparer preparer = new(stages, blobs, parallelism > 0 ? parallelism : Environment.ProcessorCount);
        List<PreparedModelData> models = [];
        foreach (ShaderModel model in archive.Models())
        {
            progress?.Report(model.Name);
            models.Add(preparer.Prepare(model, wanted is null ? null : wanted.GetValueOrDefault(model.Name) ?? new HashSet<int>()));
        }

        PreparedWriter.Write(output, models, blobs);
        return new PreparationResult(models.Count, models.Sum(m => m.Programs.Count), preparer.StageCount, stages.Decompiled, [.. preparer.Failures]);
    }
}
