using BfresSharp.Model.Material;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Materials;
using BfShaderSharp.Pack;

namespace BfShaderSharp.Assets;

/// <summary>The shader archives of a game as program selectors, by archive name, with a hash of each archive's source.</summary>
/// <remarks>
/// Selection needs only the option tables of an archive, so it runs from the BFSHA directly and does not wait for the
/// archive to be compiled to GLSL; the program numbers are the same in the prepared file. An archive that is not in the set
/// selects -1. Sources lists the name and content hash of each archive, which a pack records so a program number can be
/// checked against the archive it came from. Safe to call from many threads.
/// </remarks>
public sealed class ShaderArchiveSet
{
    private readonly Dictionary<string, MaterialShaderSelector> _selectors = [];
    private readonly List<(string Name, BlobKey Source)> _sources = [];

    public IReadOnlyList<(string Name, BlobKey Source)> Sources => _sources;

    public static ShaderArchiveSet Load(RomfsFiles romfs, IReadOnlyDictionary<ulong, string> names)
    {
        ShaderArchiveSet set = new();
        foreach (string path in romfs.ShaderFiles())
        {
            byte[] bytes = romfs.Read(path);
            BfshaFile archive = BfshaFile.Read(bytes);
            set._selectors[archive.Name] = MaterialShaderSelector.For(archive, names);
            set._sources.Add((archive.Name, BlobKey.Of(bytes)));
        }

        return set;
    }

    public int Select(Material material) =>
        _selectors.TryGetValue(material.ShaderAssign.ShaderArchiveName, out MaterialShaderSelector? selector) ? selector.Select(material) : -1;
}
