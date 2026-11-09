using BfresSharp;
using BfresSharp.Model;
using BfShaderSharp.Pack;

namespace BfShaderSharp.Assets;

/// <summary>Prepares one model file that is not in the game's dump, such as a modded model, into an asset pack of its own.</summary>
/// <remarks>
/// The model goes through the importers that prepare the dump's models, so its materials pick their shader programs from the
/// game's shader archives and its uniform blocks, animations and geometry are stored exactly as a dump model's are. The
/// textures it samples come first from the texture files given with it and the BNTX files embedded in the model file, then
/// from the romfs by name, then through the unify table; a name found nowhere is reported as missing. The pack holds only this
/// model, so a host opens it beside the pack of the game. The library writes nowhere but the stream it is given.
/// </remarks>
public static class ModelAssetPreparer
{
    private const string BntxMagic = "BNTX";

    public static AssetResult Prepare(AssetOptions options, string name, ReadOnlyMemory<byte> model, IReadOnlyList<byte[]> textureFiles, Stream output, Action<ProgramUse>? modelsDone = null)
    {
        using RomfsFiles romfs = new(options.Romfs);
        Dictionary<ulong, string> names = romfs.ExternalNames();
        ShaderArchiveSet shaders = ShaderArchiveSet.Load(romfs, names);
        PackWriter pack = new(output, options.Quality);
        AssetContext context = new(pack, romfs, shaders, names, options.Storage, options.LinearLevel) { Unify = UnifyTex.Load(romfs) };
        foreach ((string archive, BlobKey source) in shaders.Sources) pack.AddEntry(PackEntryKind.ShaderSource, archive, source);
        ModelFileImporter models = new(context);
        byte[] bytes = model.ToArray();
        models.Import(name, name, () => bytes);
        modelsDone?.Invoke(context.Programs);
        int textures = options.Textures == TextureSelection.None ? 0 : ImportTextures(context, bytes, textureFiles);
        (int fromRomfs, List<string> missing) = options.Textures == TextureSelection.None ? (0, []) : AssetPreparer.ImportTextures(context, options with { Textures = TextureSelection.Used }, new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism > 0 ? options.Parallelism : Environment.ProcessorCount });
        pack.Finish();
        return new AssetResult(1, models.Models, models.Materials, textures + fromRomfs, models.Animations, models.Skipped, pack.AddedBlobs, pack.UniqueBlobs, pack.StoredBytes, models.Failures, missing);
    }

    // The textures that came with the model: given files, then the BNTX files embedded in the model file.
    private static int ImportTextures(AssetContext context, byte[] model, IReadOnlyList<byte[]> given)
    {
        int count = 0;
        foreach (byte[] file in given) count += TextureImporter.Import(context, file, "texture file");
        if (BfresFile.IsPackage(model)) return count;
        foreach (ExternalFile embedded in BfresFile.Read(model).ExternalFiles.Values.Where(f => f.Data.Span.StartsWith(System.Text.Encoding.ASCII.GetBytes(BntxMagic))))
            count += TextureImporter.Import(context, embedded.Data.ToArray(), "embedded texture file");
        return count;
    }
}
