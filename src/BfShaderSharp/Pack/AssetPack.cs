using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using BfShaderSharp.Errors;
using BfShaderSharp.Pack.Records;
using Microsoft.Win32.SafeHandles;

namespace BfShaderSharp.Pack;

/// <summary>An asset pack: models, materials, textures and the blobs of geometry and pixels they refer to.</summary>
/// <remarks>
/// Opening reads the header and the two tables, which are small, and nothing else. A blob is read from the file when
/// asked for, so a pack of many gigabytes costs only what is used and may be larger than memory can map. Records are
/// found by key, entries by kind and name. The reader is safe to use from many threads, and decompresses Brotli blobs;
/// a texture frame is returned as the game stored it, for the caller to decompress.
/// </remarks>
public sealed class AssetPack : IDisposable
{
    private readonly SafeFileHandle _file;
    private readonly long _length;
    private readonly Dictionary<BlobKey, BlobRow> _blobs;
    private readonly List<PackEntry> _entries;
    private readonly Dictionary<(PackEntryKind, string), PackEntry> _byName = [];

    private readonly record struct BlobRow(long Offset, int Stored, int Raw, BlobCodec Codec, uint Dictionary);

    private AssetPack(SafeFileHandle file, long length, Dictionary<BlobKey, BlobRow> blobs, List<PackEntry> entries)
    {
        _file = file;
        _length = length;
        _blobs = blobs;
        _entries = entries;
        foreach (PackEntry entry in entries) _byName[(entry.Kind, entry.Name)] = entry;
    }

    public int BlobCount => _blobs.Count;

    public long Length => _length;

    public IReadOnlyList<PackEntry> Entries => _entries;

    public static AssetPack Open(string path)
    {
        SafeFileHandle file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        try
        {
            long length = RandomAccess.GetLength(file);
            byte[] header = Read(file, 0, PackLayout.HeaderSize, length);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != PackLayout.Magic) throw new BfShaderFormatException("Not an asset pack", 0);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != PackLayout.Version) throw new BfShaderUnsupportedException("Asset pack version is not supported", 4);
            if (BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(0x20)) != length) throw new BfShaderFormatException("Asset pack size does not match its header", 0x20);
            int entryCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
            int blobCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12));
            long blobTable = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(0x10));
            long entryTable = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(0x18));
            Dictionary<BlobKey, BlobRow> blobs = ReadBlobs(file, length, blobTable, blobCount, entryTable);
            byte[] entries = Read(file, entryTable, checked((int)(length - entryTable)), length);
            return new AssetPack(file, length, blobs, ReadEntries(entries, entryCount, entryTable));
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public void Dispose() => _file.Dispose();

    public PackEntry? Find(PackEntryKind kind, string name) => _byName.TryGetValue((kind, name), out PackEntry entry) ? entry : null;

    public IEnumerable<PackEntry> Of(PackEntryKind kind) => _entries.Where(e => e.Kind == kind);

    public bool Has(BlobKey key) => _blobs.ContainsKey(key);

    // The bytes of a blob. Raw and Brotli blobs come back decoded; a zstd frame comes back as stored.
    public BlobData Blob(BlobKey key)
    {
        if (!_blobs.TryGetValue(key, out BlobRow row)) throw new BfShaderFormatException($"The pack has no blob {key}");
        byte[] stored = Read(_file, row.Offset, row.Stored, _length);
        switch (row.Codec)
        {
            case BlobCodec.Raw:
                return new BlobData(stored, BlobCodec.Raw, row.Raw, 0);
            case BlobCodec.ZstdFrame:
                return new BlobData(stored, BlobCodec.ZstdFrame, row.Raw, row.Dictionary);
            case BlobCodec.Brotli:
                byte[] raw = new byte[row.Raw];
                if (!BrotliDecoder.TryDecompress(stored, raw, out int written) || written != raw.Length) throw new BfShaderFormatException($"Blob {key} does not decompress", row.Offset);
                return new BlobData(raw, BlobCodec.Raw, row.Raw, 0);
            default:
                throw new BfShaderUnsupportedException($"Blob codec {row.Codec} is not supported", row.Offset);
        }
    }

    public byte[] Bytes(BlobKey key) => Blob(key).Bytes;

    public T Record<T>(BlobKey key, Func<RecordReader, T> read) => read(new RecordReader(Bytes(key)));

    public ModelRecord? Model(string name) => Find(PackEntryKind.Model, name) is { } entry ? Record(entry.Key, ModelRecord.Read) : null;

    public TextureRecord? Texture(string name) => Find(PackEntryKind.Texture, name) is { } entry ? Record(entry.Key, TextureRecord.Read) : null;

    public SkeletalAnimRecord? SkeletalAnim(string name) => Find(PackEntryKind.SkeletalAnim, name) is { } entry ? Record(entry.Key, SkeletalAnimRecord.Read) : null;

    public MaterialAnimRecord? MaterialAnim(string name) => Find(PackEntryKind.MaterialAnim, name) is { } entry ? Record(entry.Key, MaterialAnimRecord.Read) : null;

    public VisibilityAnimRecord? VisibilityAnim(string name) => Find(PackEntryKind.VisibilityAnim, name) is { } entry ? Record(entry.Key, VisibilityAnimRecord.Read) : null;

    public ShapeAnimRecord? ShapeAnim(string name) => Find(PackEntryKind.ShapeAnim, name) is { } entry ? Record(entry.Key, ShapeAnimRecord.Read) : null;

    public MaterialRecord Material(BlobKey key) => Record(key, MaterialRecord.Read);

    public UniformLayoutRecord UniformLayout(BlobKey key) => Record(key, UniformLayoutRecord.Read);

    public ShapeRecord Shape(BlobKey key) => Record(key, ShapeRecord.Read);

    public SkeletonRecord Skeleton(BlobKey key) => Record(key, SkeletonRecord.Read);

    private static Dictionary<BlobKey, BlobRow> ReadBlobs(SafeFileHandle file, long length, long table, int count, long end)
    {
        if (count < 0 || table < PackLayout.HeaderSize || table + (long)count * PackLayout.BlobRowSize > end) throw new BfShaderFormatException("Blob table runs past its place in the file", table);
        byte[] rows = Read(file, table, count * PackLayout.BlobRowSize, length);
        Dictionary<BlobKey, BlobRow> blobs = new(count);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> row = rows.AsSpan(i * PackLayout.BlobRowSize, PackLayout.BlobRowSize);
            long offset = BinaryPrimitives.ReadInt64LittleEndian(row[16..]);
            int stored = BinaryPrimitives.ReadInt32LittleEndian(row[24..]);
            if (offset < PackLayout.HeaderSize || stored < 0 || offset + stored > table) throw new BfShaderFormatException($"Blob {i} lies outside the data", table + (long)i * PackLayout.BlobRowSize);
            BlobKey key = new(BinaryPrimitives.ReadUInt64LittleEndian(row), BinaryPrimitives.ReadUInt64LittleEndian(row[8..]));
            blobs[key] = new BlobRow(offset, stored, BinaryPrimitives.ReadInt32LittleEndian(row[28..]), (BlobCodec)row[32], BinaryPrimitives.ReadUInt32LittleEndian(row[36..]));
        }

        return blobs;
    }

    private static List<PackEntry> ReadEntries(byte[] bytes, int count, long position)
    {
        List<PackEntry> entries = new(Math.Max(0, Math.Min(count, bytes.Length / 19)));
        int at = 0;
        for (int i = 0; i < count; i++)
        {
            if (at + 3 > bytes.Length) throw new BfShaderFormatException("Entry table is truncated", position + at);
            PackEntryKind kind = (PackEntryKind)bytes[at];
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 1));
            if (at + 3 + nameLength + BlobKey.Size > bytes.Length) throw new BfShaderFormatException("Entry table is truncated", position + at);
            string name = Encoding.UTF8.GetString(bytes, at + 3, nameLength);
            at += 3 + nameLength;
            BlobKey key = new(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at)), BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at + 8)));
            entries.Add(new PackEntry(kind, name, key));
            at += BlobKey.Size;
        }

        return entries;
    }

    private static byte[] Read(SafeFileHandle file, long offset, int size, long length)
    {
        if (offset < 0 || size < 0 || offset + size > length) throw new BfShaderFormatException($"Read of {size} bytes runs past the end of the {length}-byte pack", offset);
        byte[] bytes = new byte[size];
        if (RandomAccess.Read(file, bytes, offset) != size) throw new BfShaderFormatException("The pack ended while it was being read", offset);
        return bytes;
    }
}
