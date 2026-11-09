using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace BfShaderSharp.Pack;

/// <summary>Writes a pack: blobs stored once by content, and the named entries that point at them.</summary>
/// <remarks>
/// <see cref="Add"/> and <see cref="AddFrame"/> are safe to call from many threads. Hashing and compression run outside
/// the lock, a blob already stored is not written again, and the data goes to the stream as it arrives, so memory holds
/// only the blobs in flight. The same contents are stored once however many models name them; the table of blobs and the
/// entries are written in key and name order, so what a pack holds does not depend on the thread count, though the data
/// is laid out in arrival order. <see cref="Finish"/> writes the tables and the header and must be called last, once.
/// </remarks>
public sealed class PackWriter(Stream output, int quality = 4)
{
    private readonly Dictionary<BlobKey, Row> _rows = [];
    private readonly List<PackEntry> _entries = [];
    private readonly object _gate = new();
    private long _position = Begin(output);
    private long _added;

    private readonly record struct Row(long Offset, int Stored, int Raw, BlobCodec Codec, uint Dictionary);

    public int UniqueBlobs
    {
        get
        {
            lock (_gate) return _rows.Count;
        }
    }

    public long AddedBlobs => Interlocked.Read(ref _added);

    public long StoredBytes
    {
        get
        {
            lock (_gate) return _position - PackLayout.HeaderSize;
        }
    }

    // Stores bytes under the hash of their contents, Brotli-compressed or as they are, and returns the key.
    public BlobKey Add(ReadOnlySpan<byte> raw, BlobCodec codec = BlobCodec.Brotli)
    {
        BlobKey key = BlobKey.Of(raw);
        Interlocked.Increment(ref _added);
        if (Has(key)) return key;
        if (codec == BlobCodec.Raw || raw.Length < 64) return Store(key, raw, raw.Length, BlobCodec.Raw, 0);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BrotliEncoder.GetMaxCompressedLength(raw.Length));
        try
        {
            if (!BrotliEncoder.TryCompress(raw, buffer, out int written, quality, 22)) throw new InvalidOperationException("Brotli compression failed");
            return written < raw.Length ? Store(key, buffer.AsSpan(0, written), raw.Length, BlobCodec.Brotli, 0) : Store(key, raw, raw.Length, BlobCodec.Raw, 0);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Stores a zstd frame untouched under the hash of the frame, with the size it decompresses to and the dictionary its header names.
    public BlobKey AddFrame(ReadOnlySpan<byte> frame, int rawSize, uint dictionaryId)
    {
        BlobKey key = BlobKey.Of(frame);
        Interlocked.Increment(ref _added);
        return Has(key) ? key : Store(key, frame, rawSize, BlobCodec.ZstdFrame, dictionaryId);
    }

    public void AddEntry(PackEntryKind kind, string name, BlobKey key)
    {
        lock (_gate) _entries.Add(new PackEntry(kind, name, key));
    }

    public void Finish()
    {
        lock (_gate)
        {
            long blobTable = _position;
            Span<byte> bytes = stackalloc byte[PackLayout.BlobRowSize];
            foreach (var (key, row) in _rows.OrderBy(r => r.Key))
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes, key.High);
                BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], key.Low);
                BinaryPrimitives.WriteInt64LittleEndian(bytes[16..], row.Offset);
                BinaryPrimitives.WriteInt32LittleEndian(bytes[24..], row.Stored);
                BinaryPrimitives.WriteInt32LittleEndian(bytes[28..], row.Raw);
                bytes[32] = (byte)row.Codec;
                BinaryPrimitives.WriteUInt32LittleEndian(bytes[36..], row.Dictionary);
                output.Write(bytes);
            }

            long entryTable = blobTable + (long)_rows.Count * PackLayout.BlobRowSize;
            foreach (PackEntry entry in _entries.OrderBy(e => e.Kind).ThenBy(e => e.Name, StringComparer.Ordinal)) WriteEntry(entry);
            WriteHeader(blobTable, entryTable);
        }
    }

    private BlobKey Store(BlobKey key, ReadOnlySpan<byte> stored, int rawSize, BlobCodec codec, uint dictionary)
    {
        lock (_gate)
        {
            if (_rows.ContainsKey(key)) return key;
            _rows[key] = new Row(_position, stored.Length, rawSize, codec, dictionary);
            output.Write(stored);
            _position += stored.Length;
        }

        return key;
    }

    private bool Has(BlobKey key)
    {
        lock (_gate) return _rows.ContainsKey(key);
    }

    private void WriteEntry(PackEntry entry)
    {
        byte[] name = Encoding.UTF8.GetBytes(entry.Name);
        output.WriteByte((byte)entry.Kind);
        output.Write(BitConverter.GetBytes((ushort)name.Length));
        output.Write(name);
        output.Write(BitConverter.GetBytes(entry.Key.High));
        output.Write(BitConverter.GetBytes(entry.Key.Low));
    }

    private void WriteHeader(long blobTable, long entryTable)
    {
        byte[] header = new byte[PackLayout.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, PackLayout.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), PackLayout.Version);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), _entries.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), _rows.Count);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(0x10), blobTable);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(0x18), entryTable);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(0x20), output.Position);
        output.Position = 0;
        output.Write(header);
        output.Seek(0, SeekOrigin.End);
    }

    private static long Begin(Stream output)
    {
        output.Write(new byte[PackLayout.HeaderSize]);
        return PackLayout.HeaderSize;
    }
}
