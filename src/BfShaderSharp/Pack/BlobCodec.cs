namespace BfShaderSharp.Pack;

/// <summary>How the bytes of a blob are stored in a pack.</summary>
/// <remarks>
/// Raw and Brotli blobs are decoded by the reader. A ZstdFrame blob is a zstd frame kept exactly as the game stored it, so
/// preparing it costs no compression; the reader hands the frame to the caller with the dictionary id its header names.
/// </remarks>
public enum BlobCodec : byte
{
    Raw = 0,
    Brotli = 1,
    ZstdFrame = 2,
}
