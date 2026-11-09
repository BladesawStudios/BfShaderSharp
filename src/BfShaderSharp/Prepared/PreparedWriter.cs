using BfShaderSharp.Binary;

namespace BfShaderSharp.Prepared;

/// <summary>Writes shading models and their blobs as one prepared file.</summary>
/// <remarks>
/// The file is the header, the tables of every model, the model table, the string table and the blob table, then
/// the compressed blobs. The tables are built in memory and the blobs are streamed from the pool, so a file
/// of gigabytes of GLSL needs memory for its tables and the pool only. The writer reads the pool when called and
/// leaves it unchanged.
/// </remarks>
public static class PreparedWriter
{
    public static void Write(Stream output, IReadOnlyList<PreparedModelData> models, BlobPool blobs)
    {
        ByteSink sink = new();
        sink.Bytes(new byte[PreparedLayout.HeaderSize]);
        StringPool strings = new();
        int[] position = blobs.CanonicalOrder(out int[] stored);
        ModelSerializer serializer = new(sink, strings, position);
        foreach (PreparedModelData model in models) serializer.Write(model);
        sink.Align(8);
        long modelTable = sink.Length;
        foreach (long record in serializer.ModelOffsets) sink.U64((ulong)record);
        sink.Align(8);
        long stringTable = sink.Length;
        strings.Write(sink);
        sink.Align(8);
        long blobTable = sink.Length;
        int blobCount = blobs.Count;
        long dataStart = (blobTable + 8 + (long)blobCount * PreparedLayout.BlobEntrySize + 7) / 8 * 8;
        sink.U32((uint)blobCount);
        sink.U32(0);
        long offset = dataStart;
        for (int i = 0; i < blobCount; i++)
        {
            var (compressed, rawSize) = blobs[stored[i]];
            sink.U64((ulong)offset);
            sink.U32((uint)compressed.Length);
            sink.U32((uint)rawSize);
            offset += (compressed.Length + 7) / 8 * 8;
        }

        sink.Align(8);
        PatchHeader(sink, models.Count, modelTable, stringTable, blobTable, dataStart, offset);
        output.Write(sink.Written);
        byte[] padding = new byte[8];
        for (int i = 0; i < blobCount; i++)
        {
            byte[] compressed = blobs[stored[i]].Compressed;
            output.Write(compressed);
            output.Write(padding, 0, (8 - compressed.Length % 8) % 8);
        }
    }

    private static void PatchHeader(ByteSink sink, int modelCount, long modelTable, long stringTable, long blobTable, long dataStart, long fileSize)
    {
        sink.PatchU32(0, PreparedLayout.Magic);
        sink.PatchU32(4, PreparedLayout.Version);
        sink.PatchU32(8, (uint)modelCount);
        sink.PatchU64(0x10, (ulong)modelTable);
        sink.PatchU64(0x18, (ulong)stringTable);
        sink.PatchU64(0x20, (ulong)blobTable);
        sink.PatchU64(0x28, (ulong)dataStart);
        sink.PatchU64(0x30, (ulong)fileSize);
    }
}
