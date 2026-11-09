using System.Collections.Concurrent;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;

namespace BfShaderSharp.Assets;

/// <summary>What every importer of one preparation shares: the pack being written, the romfs, the shader archives and the name table.</summary>
/// <remarks>
/// UsedTextures collects the names of the textures that imported materials sample, so the texture step can import only
/// those, and Programs the shader programs they run. All members are safe to use from many threads.
/// </remarks>
public sealed class AssetContext(PackWriter pack, RomfsFiles romfs, ShaderArchiveSet shaders, IReadOnlyDictionary<ulong, string> names, TextureStorage storage = TextureStorage.Native, int linearLevel = 3)
{
    public PackWriter Pack { get; } = pack;

    public RomfsFiles Romfs { get; } = romfs;

    public ShaderArchiveSet Shaders { get; } = shaders;

    public IReadOnlyDictionary<ulong, string> Names { get; } = names;

    public ConcurrentDictionary<string, byte> UsedTextures { get; } = new(StringComparer.Ordinal);

    public TextureStorage Storage { get; } = storage;

    public int LinearLevel { get; } = linearLevel;

    public ProgramUse Programs { get; } = new();
}
