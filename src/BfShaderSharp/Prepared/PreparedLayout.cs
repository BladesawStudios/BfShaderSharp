namespace BfShaderSharp.Prepared;

/// <summary>The byte layout of a prepared file, described once for the writer and the reader.</summary>
/// <remarks>
/// Everything is little-endian and every table starts on an 8-byte boundary. The header holds the position of the
/// model table, the string table and the blob table. Strings are referred to by index and blobs by index. A blob is
/// a Brotli stream of GLSL or of a default uniform buffer. Record sizes below are the stride of each table, and
/// the field offsets used by the reader and writer are the ones listed next to them.
/// </remarks>
internal static class PreparedLayout
{
    internal const uint Magic = 0x50534642; // "BFSP"
    internal const uint Version = 2;
    internal const int StageCount = 8;
    internal const int NoBlob = -1;

    // Header: magic, version, model count, flags, then u64 model table, string table, blob table, blob data, file size.
    internal const int HeaderSize = 0x40;

    // Model: name, program, row word, option, variation, sampler, attribute and block counts, then u64 offsets of the option table,
    // key rows, variation table, program variation ids, bindings, samplers, attributes and blocks.
    internal const int ModelSize = 0x60;

    // Option: name, mask, word (u16), shift (u8), default choice, choice count, u64 offset of the choice string ids.
    internal const int OptionSize = 0x20;

    // Sampler: name, annotation, index.
    internal const int SamplerSize = 0x10;

    // Attribute: name, index, location.
    internal const int AttributeSize = 0x10;

    // Block: name, index (u16), type (u16), byte size, uniform count, u64 uniform offset, default buffer blob.
    internal const int BlockSize = 0x20;

    // Uniform: name, index, offset, block index.
    internal const int UniformSize = 0x10;

    // Blob table entry: u64 offset, u32 compressed size, u32 raw size.
    internal const int BlobEntrySize = 0x10;

    // Variation: one blob index per stage.
    internal const int VariationSize = StageCount * 4;
}
