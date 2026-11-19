using System.Collections.Concurrent;
using SarcSharp;

namespace BfShaderSharp.Assets;

/// <summary>Reads files that sit inside a SARC pack of the romfs, named as <c>Pack/Bootup_Graphics.pack//Shader/uking_mat.product.sbfsha</c>: the pack's path, two slashes, the entry.</summary>
/// <remarks>
/// A pack is opened once and kept, and its entries are returned as the bytes the pack holds; the caller decodes them. Safe to call from many threads.
/// </remarks>
public sealed class PackedFiles
{
    public const string Separator = "//";
    private readonly ConcurrentDictionary<string, Lazy<SarcFile>> _packs = new();
    private readonly Func<string, byte[]> _readPack;

    /// <summary>Creates the reader; readPack returns a pack file's plain bytes.</summary>
    public PackedFiles(Func<string, byte[]> readPack) => _readPack = readPack;

    public static bool IsNested(string path) => path.Contains(Separator, StringComparison.Ordinal);

    public static (string Pack, string Entry) Split(string path)
    {
        int at = path.IndexOf(Separator, StringComparison.Ordinal);
        return (path[..at], path[(at + Separator.Length)..]);
    }

    public static string Join(string pack, string entry) => pack + Separator + entry;

    /// <summary>The names of the entries of a pack that start with the prefix, in the pack's order.</summary>
    public IEnumerable<string> Entries(string pack, string prefix) =>
        Open(pack).Entries.Select(e => e.Name).Where(name => name.StartsWith(prefix, StringComparison.Ordinal));

    public byte[] Read(string path)
    {
        (string pack, string entry) = Split(path);
        return Open(pack)[entry]?.Data ?? throw new FileNotFoundException($"{entry} is not in {pack}.");
    }

    private SarcFile Open(string pack) => _packs.GetOrAdd(pack, p => new Lazy<SarcFile>(() => SarcFile.FromBinary(_readPack(p)))).Value;
}
