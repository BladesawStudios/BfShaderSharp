using BfShaderSharp.Pack;

namespace BfShaderSharp.Assets;

/// <summary>Prepares the models, materials and textures of a romfs into one asset pack.</summary>
/// <remarks>
/// Model files are imported in parallel, each choosing its materials' shader programs from the shader archives as it
/// goes, then the textures the materials sample (or all of them) are imported in parallel. Everything is stored by content
/// hash into the pack as it is produced, so memory holds only the files in flight. Any number of model files may be
/// selected, from one to all. modelsDone is called once every model is imported and before the textures are, with the
/// programs the materials run, so the shader archives can be prepared for them while the textures are read. The library writes nowhere but the stream it is given.
/// </remarks>
public static class AssetPreparer
{
    public static AssetResult Prepare(AssetOptions options, Stream output, Action<ProgramUse>? modelsDone = null)
    {
        using RomfsFiles romfs = new(options.Romfs);
        Dictionary<ulong, string> names = romfs.ExternalNames();
        ShaderArchiveSet shaders = ShaderArchiveSet.Load(romfs, names);
        PackWriter pack = new(output, options.Quality);
        AssetContext context = new(pack, romfs, shaders, names);
        foreach ((string name, BlobKey source) in shaders.Sources) pack.AddEntry(PackEntryKind.ShaderSource, name, source);
        ParallelOptions parallel = new() { MaxDegreeOfParallelism = options.Parallelism > 0 ? options.Parallelism : Environment.ProcessorCount };
        ModelFileImporter models = new(context);
        IReadOnlyList<string> files = romfs.ModelFiles(options.ModelPatterns);
        Parallel.ForEach(files, parallel, models.Import);
        modelsDone?.Invoke(context.Programs);
        (int textures, List<string> missing) = ImportTextures(context, options, parallel);
        pack.Finish();
        return new AssetResult(files.Count, models.Models, models.Materials, textures, models.Skipped, pack.AddedBlobs, pack.UniqueBlobs, pack.StoredBytes, models.Failures, missing);
    }

    private static (int Textures, List<string> Missing) ImportTextures(AssetContext context, AssetOptions options, ParallelOptions parallel)
    {
        List<string> missing = [];
        IReadOnlyList<string> paths = options.Textures switch
        {
            TextureSelection.All => context.Romfs.TextureFiles(),
            TextureSelection.None => [],
            _ => [.. context.UsedTextures.Keys.Order(StringComparer.Ordinal).Select(name => context.Romfs.TextureFile(name) ?? Missing(missing, name)).OfType<string>().Distinct()],
        };
        int textures = 0;
        Parallel.ForEach(paths, parallel, path => Interlocked.Add(ref textures, TextureImporter.Import(context, path)));
        return (textures, missing);
    }

    private static string? Missing(List<string> missing, string name)
    {
        missing.Add(name);
        return null;
    }
}
