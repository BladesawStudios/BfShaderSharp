using System.IO.Compression;
using System.Security.Cryptography;

namespace BfShaderSharp.Prepared;

/// <summary>Collects the blobs of a prepared file, compressing each once and storing identical contents once.</summary>
/// <remarks>
/// <see cref="Add"/> is safe to call from many threads: the compression runs outside the lock, and two threads adding
/// the same contents at once keep one copy. A blob's number is its place in insertion order; the writer stores blobs in content order and renumbers references. Compression is Brotli at
/// the quality given to the constructor.
/// </remarks>
public sealed class BlobPool(int quality = 9)
{
    public const int None = PreparedLayout.NoBlob;

    private readonly Dictionary<string, int> _index = [];
    private readonly List<(byte[] Compressed, int RawSize)> _blobs = [];
    private readonly List<string> _keys = [];
    private readonly object _gate = new();

    public int Count
    {
        get
        {
            lock (_gate) return _blobs.Count;
        }
    }

    public int Add(ReadOnlySpan<byte> raw)
    {
        string key = Convert.ToBase64String(SHA256.HashData(raw));
        lock (_gate)
        {
            if (_index.TryGetValue(key, out int existing)) return existing;
        }

        byte[] compressed = Compress(raw, quality);
        lock (_gate)
        {
            if (_index.TryGetValue(key, out int existing)) return existing;
            _blobs.Add((compressed, raw.Length));
            _keys.Add(key);
            return _index[key] = _blobs.Count - 1;
        }
    }

    // The blob numbers in the order the file stores them: sorted by content hash, so a file does not depend on the order threads added blobs.
    // Entry i of the result is the file position of the blob added as number i.
    internal int[] CanonicalOrder(out int[] sorted)
    {
        lock (_gate)
        {
            sorted = [.. Enumerable.Range(0, _blobs.Count).OrderBy(i => _keys[i], StringComparer.Ordinal)];
        }

        int[] position = new int[sorted.Length];
        for (int rank = 0; rank < sorted.Length; rank++) position[sorted[rank]] = rank;
        return position;
    }

    internal (byte[] Compressed, int RawSize) this[int index]
    {
        get
        {
            lock (_gate) return _blobs[index];
        }
    }

    internal static byte[] Compress(ReadOnlySpan<byte> raw, int quality)
    {
        byte[] buffer = new byte[BrotliEncoder.GetMaxCompressedLength(raw.Length)];
        if (!BrotliEncoder.TryCompress(raw, buffer, out int written, quality, 22)) throw new InvalidOperationException("Brotli compression failed");
        return buffer.AsSpan(0, written).ToArray();
    }
}
