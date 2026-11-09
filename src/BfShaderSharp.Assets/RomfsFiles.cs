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

    public RomfsFiles(string root)
    {
        Root = root;
        _dictionaries = File.Exists(Path.Combine(root, "Pack", "ZsDic.pack.zs")) ? ZsDic.FromRomfs(root) : ZsDic.Dictionaryless();
    }

    public string Root { get; }

    public ZsDic Dictionaries => _dictionaries;

    public void Dispose() => _dictionaries.Dispose();

    // Model files whose name (without its extensions) matches any pattern; every model file when there are none. A pattern may use * and ?.
    public IReadOnlyList<string> ModelFiles(IReadOnlyList<string> patterns) =>
        [.. Files("Model", "*.bfres*").Where(path => patterns.Count == 0 || patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, StemOf(path), true))).Order(StringComparer.Ordinal)];

    public IReadOnlyList<string> TextureFiles() => [.. Files("Tex", "*.bntx*").Order(StringComparer.Ordinal)];

    public IReadOnlyList<string> ShaderFiles() => [.. Files("Shader", "*.bfsha*").Order(StringComparer.Ordinal)];

    public string? TextureFile(string name) =>
        new[] { ".bntx.zs", ".bntx" }.Select(extension => Path.Combine(Root, "Tex", name + extension)).FirstOrDefault(File.Exists);

    // The file name up to its first dot: Land_A.Land_A_01.bfres.zs is Land_A.
    public static string StemOf(string path)
    {
        string name = Path.GetFileName(path);
        int dot = name.IndexOf('.');
        return dot < 0 ? name : name[..dot];
    }

    public byte[] Read(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        return ZsDic.IsCompressed(data) ? _dictionaries.Decompress(data, path) : data;
    }

    public Dictionary<ulong, string> ExternalNames()
    {
        string? table = Files("Shader", "ExternalBinaryString.bfres*").FirstOrDefault();
        return table is null ? [] : BfresFile.Read(Read(table)).NamesByHash();
    }

    private IEnumerable<string> Files(string folder, string pattern)
    {
        string path = Path.Combine(Root, folder);
        return Directory.Exists(path) ? Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly) : [];
    }
}
