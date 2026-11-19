using BfShaderSharp.Pack;

namespace BfShaderSharp.Assets;

/// <summary>Prepares the data a game's scenes need that belongs to no model: for now the cloud dome's masks, each stored as a game data entry of one pack.</summary>
/// <remarks>The library writes nowhere but the stream it is given. A game that lacks a kind of data has no entries of that kind, and the result says how many entries each kind has.</remarks>
public static class SystemAssetPreparer
{
    public static IReadOnlyDictionary<string, int> Prepare(string romfs, Stream output)
    {
        using RomfsFiles files = new(romfs);
        PackWriter pack = new(output);
        AssetContext context = new(pack, files, ShaderArchiveSet.Empty, new Dictionary<ulong, string>());
        Dictionary<string, int> counts = new() { [CloudMaskImporter.Group] = CloudMaskImporter.Import(context) };
        pack.Finish();
        return counts;
    }
}
