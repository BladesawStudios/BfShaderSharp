using System.IO.Enumeration;
using BfresSharp;
using ZsDicSharp;

namespace BfShaderSharp.Assets;

/// <summary>The files of a game's romfs that preparation reads: its models, textures and shader archives, with the game's dictionaries.</summary>
/// <remarks>
/// A romfs holds Model, Tex and Shader folders and the Pack/ZsDic.pack.zs that its compressed files need. Reads return
/// the decompressed bytes. The external string table of the Shader folder maps the name hashes of Switch 2 files back to
/// names; a game without one has an empty map. Safe to use from many threads.
/// </remarks>
public sealed class RomfsFiles : IDisposable
{
    private readonly ZsDic _dictionaries;
    private readonly PackedFiles _packs;

    public RomfsFiles(string root)
    {
        Root = root;
        _dictionaries = File.Exists(Path.Combine(root, "Pack", "ZsDic.pack.zs")) ? ZsDic.FromRomfs(root) : ZsDic.Dictionaryless();
        _packs = new PackedFiles(pack => Read(Path.Combine(root, pack)));
    }

    public string Root { get; }

    public ZsDic Dictionaries => _dictionaries;

    public void Dispose() => _dictionaries.Dispose();

    // Model files whose name (without its extensions) matches any pattern; every model file when there are none. A pattern may use * and ?.
    public IReadOnlyList<string> ModelFiles(IReadOnlyList<string> patterns) =>
        [.. Files("Model", "*.bfres*").Concat(Files("Model", "*.sbfres")).Concat(PackedModels()).Where(path => patterns.Count == 0 || patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, StemOf(path), true))).Order(StringComparer.Ordinal)];

    // Tex holds BNTX files; a game with a TexToGo folder (Tears of the Kingdom) keeps its material textures there as TXTG or BNTX files.
    public IReadOnlyList<string> TextureFiles() => [.. Files("Tex", "*.bntx*").Concat(Files("TexToGo", "*.txtg")).Concat(Files("TexToGo", "*.bntx*")).Order(StringComparer.Ordinal)];

    // A game that keeps its shader archives in Pack/Bootup_Graphics.pack (Breath of the Wild) has them listed as files inside that pack.
    public IReadOnlyList<string> ShaderFiles() => [.. Files("Shader", "*.bfsha*").Concat(PackedShaders()).Order(StringComparer.Ordinal)];

    // The files of a model's own textures: a Breath of the Wild model keeps them as BNTX files inside its Tex.sbfres, Tex1.sbfres and so on.
    public IReadOnlyList<string> ModelTextureFiles(string stem) =>
        [.. Files("Model", stem + ".Tex*.sbfres").Where(path => StemOf(path) == stem).Order(StringComparer.Ordinal)];

    public string? TextureFile(string name) =>
        new[] { ("Tex", ".bntx.zs"), ("Tex", ".bntx"), ("TexToGo", ".txtg"), ("TexToGo", ".bntx.zs"), ("TexToGo", ".bntx") }
            .Select(place => Path.Combine(Root, place.Item1, name + place.Item2)).FirstOrDefault(File.Exists);

    // The baked-lighting tile files of a game that has them (the Bake/Scene folder of Tears of the Kingdom), in name order.
    public IReadOnlyList<string> BakeTiles() => [.. Files("Bake/Scene", "*.bkres.zs").Order(StringComparer.Ordinal)];

    // The file name up to its first dot: Land_A.Land_A_01.bfres.zs is Land_A.
    public static string StemOf(string path)
    {
        string name = Path.GetFileName(path);
        int dot = name.IndexOf('.');
        return dot < 0 ? name : name[..dot];
    }

    public byte[] Read(string path)
    {
        byte[] data = PackedFiles.IsNested(path) ? _packs.Read(path) : File.ReadAllBytes(path);
        return GameFile.Decode(data, path, _dictionaries);
    }

    public Dictionary<ulong, string> ExternalNames()
    {
        string? table = Files("Shader", "ExternalBinaryString.bfres*").FirstOrDefault();
        return table is null ? [] : BfresFile.Read(Read(table)).NamesByHash();
    }

    private IEnumerable<string> PackedShaders() => PackedEntries("Pack/Bootup_Graphics.pack", "Shader/", ".sbfsha");

    private IEnumerable<string> PackedModels() => PackedEntries("Pack/TitleBG.pack", "Model/", ".sbfres");

    private IEnumerable<string> PackedEntries(string pack, string folder, string extension) =>
        File.Exists(Path.Combine(Root, pack)) ? _packs.Entries(pack, folder).Where(name => name.EndsWith(extension, StringComparison.Ordinal)).Select(name => PackedFiles.Join(pack, name)) : [];

    private IEnumerable<string> Files(string folder, string pattern)
    {
        string path = Path.Combine(Root, folder);
        return Directory.Exists(path) ? Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly) : [];
    }
}
