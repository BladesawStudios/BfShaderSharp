namespace BfShaderSharp.Pack;

/// <summary>The stored bytes of a blob and what is needed to decode them.</summary>
/// <remarks>For Raw and Brotli blobs <see cref="AssetPack.Blob"/> has already decoded the bytes and the codec is Raw. A ZstdFrame blob stays a frame: decompress it to <see cref="RawSize"/> bytes with the dictionary <see cref="DictionaryId"/> names, or none when it is 0.</remarks>
public readonly record struct BlobData(byte[] Bytes, BlobCodec Codec, int RawSize, uint DictionaryId);
