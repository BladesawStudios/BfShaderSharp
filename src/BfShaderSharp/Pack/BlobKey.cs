using System.IO.Hashing;

namespace BfShaderSharp.Pack;

/// <summary>The 128-bit content hash that names a blob of a pack.</summary>
/// <remarks>
/// Identical bytes have the same key whoever stores them, so records refer to each other by key and the same blob is
/// stored once. Keys order by their two halves, which is the order a pack lists its blobs in.
/// </remarks>
public readonly record struct BlobKey(ulong High, ulong Low) : IComparable<BlobKey>
{
    public const int Size = 16;

    public static BlobKey Of(ReadOnlySpan<byte> bytes)
    {
        UInt128 hash = XxHash128.HashToUInt128(bytes);
        return new BlobKey((ulong)(hash >> 64), (ulong)hash);
    }

    public int CompareTo(BlobKey other) => High != other.High ? High.CompareTo(other.High) : Low.CompareTo(other.Low);

    public override string ToString() => $"{High:x16}{Low:x16}";
}
