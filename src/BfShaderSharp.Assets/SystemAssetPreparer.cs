using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;

namespace BfShaderSharp.Assets;

/// <summary>Prepares the data a game's scenes need that belongs to no model: for now the cloud dome's masks, each stored as a game data entry of one pack.</summary>
/// <remarks>The library writes nowhere but the stream it is given. A game that lacks a kind of data has no entries of that kind, and the result says how many entries each kind has.</remarks>
public static class SystemAssetPreparer
{
    public static IReadOnlyDictionary<string, int> Prepare(string romfs, Stream output) => Prepare(romfs, output, [], 0);

    /// <summary>As <see cref="Prepare(string, Stream)"/>, also importing the baked lighting: its index, and the data of each tile named.</summary>
    public static IReadOnlyDictionary<string, int> Prepare(string romfs, Stream output, IReadOnlyCollection<string> bakeTiles, int parallelism)
    {
        using RomfsFiles files = new(romfs);
        PackWriter pack = new(output);
        AssetContext context = new(pack, files, ShaderArchiveSet.Empty, new Dictionary<ulong, string>(), TextureStorage.Linear);
        Dictionary<string, int> counts = new() { [CloudMaskImporter.Group] = CloudMaskImporter.Import(context) };
        (int placements, _) = BakeImporter.Import(context, bakeTiles, parallelism > 0 ? parallelism : Environment.ProcessorCount);
        counts["Bake placements"] = placements;
        pack.Finish();
        return counts;
    }
}
