namespace BfShaderSharp.Pack;

/// <summary>The positions and sizes of the structures of a pack file.</summary>
/// <remarks>
/// A pack is a 64-byte header, the blob data, the blob table sorted by key, then the entry table. The header holds the
/// counts, the positions of both tables and the file size. A blob table row is the key, the position, the stored and
/// raw sizes, the codec and the dictionary id. An entry is its kind, a 16-bit length and name, and its key.
/// </remarks>
internal static class PackLayout
{
    internal const uint Magic = 0x50414642; // "BFAP"
    internal const uint Version = 1;
    internal const int HeaderSize = 64;
    internal const int BlobRowSize = 40;
}
