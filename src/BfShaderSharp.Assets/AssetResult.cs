namespace BfShaderSharp.Assets;

/// <summary>What a preparation did: the files it read, what it stored, what it could not and how much deduplication saved.</summary>
/// <remarks>
/// BlobsAdded counts every blob any importer offered and BlobsStored those that were new, so the difference is what
/// deduplication saved. Failures lists files that could not be read, and MissingTextures the texture names a material
/// samples that have no file in the Tex folder.
/// </remarks>
public sealed record AssetResult(
    int ModelFiles,
    int Models,
    int Materials,
    int Textures,
    int Animations,
    int Skipped,
    long BlobsAdded,
    int BlobsStored,
    long PackBytes,
    IReadOnlyList<(string File, string Reason)> Failures,
    IReadOnlyList<string> MissingTextures);
