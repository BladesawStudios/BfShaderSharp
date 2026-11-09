using BfShaderSharp.Pack.Records;

namespace BfShaderSharp.Assets;

/// <summary>What to prepare from a romfs and how.</summary>
/// <remarks>
/// ModelPatterns select model files by the part of their name before the first dot, with * and ?; none selects every
/// model file. Textures follow <see cref="TextureSelection"/>. Parallelism 0 uses every processor. Quality is the Brotli quality of geometry, 0 to 11. Storage Linear deswizzles every texture and recompresses it with zstd at LinearLevel, which costs prepare time and size and saves the deswizzle when a texture is loaded.
/// </remarks>
public sealed record AssetOptions(string Romfs, IReadOnlyList<string> ModelPatterns, TextureSelection Textures = TextureSelection.Used, int Parallelism = 0, int Quality = 4, TextureStorage Storage = TextureStorage.Native, int LinearLevel = 3);

/// <summary>Which textures a preparation stores.</summary>
/// <remarks>Used stores the textures that prepared materials sample, All every file of the Tex folder, and None leaves textures out of the pack.</remarks>
public enum TextureSelection
{
    Used,
    All,
    None,
}
