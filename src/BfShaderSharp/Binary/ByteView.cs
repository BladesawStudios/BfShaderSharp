using System.Buffers.Binary;
using System.Text;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Binary;

/// <summary>A read-only window over little-endian file bytes that reads fixed-size values at absolute offsets.</summary>
/// <remarks>
/// Holds no position, so any number of readers can use one view at once, and slices share the bytes they were cut
/// from instead of copying them. Every read checks its range once and throws <see cref="BfShaderFormatException"/>
/// with the offending offset, never an index error from the runtime. Strings are the Switch layout: a 16-bit
/// length, then the characters.
/// </remarks>
public readonly struct ByteView(ReadOnlyMemory<byte> data)
{
    private readonly ReadOnlyMemory<byte> _data = data;

    public int Length => _data.Length;

    public ReadOnlySpan<byte> Span => _data.Span;

    public ReadOnlyMemory<byte> Memory => _data;

    public ByteView Slice(long offset, long length)
    {
        Check(offset, length);
        return new ByteView(_data.Slice((int)offset, (int)length));
    }

    public ReadOnlyMemory<byte> Bytes(long offset, long length)
    {
        Check(offset, length);
        return _data.Slice((int)offset, (int)length);
    }

    public byte U8(long offset) => Read(offset, 1)[0];

    public ushort U16(long offset) => BinaryPrimitives.ReadUInt16LittleEndian(Read(offset, 2));

    public uint U32(long offset) => BinaryPrimitives.ReadUInt32LittleEndian(Read(offset, 4));

    public int I32(long offset) => BinaryPrimitives.ReadInt32LittleEndian(Read(offset, 4));

    // A 32-bit count that fits an int, or a format error: a corrupt count never becomes an overflow.
    public int Count(long offset)
    {
        uint value = U32(offset);
        if (value > int.MaxValue) throw new BfShaderFormatException($"Count {value} is too large", offset);
        return (int)value;
    }

    // A 64-bit file position that fits a long, or a format error.
    public long Position(long offset)
    {
        ulong value = U64(offset);
        if (value > long.MaxValue) throw new BfShaderFormatException($"Position {value} is too large", offset);
        return (long)value;
    }

    public ulong U64(long offset) => BinaryPrimitives.ReadUInt64LittleEndian(Read(offset, 8));

    // The characters of the string whose length field is at the offset.
    public ReadOnlySpan<byte> StringBytes(long offset)
    {
        int length = U16(offset);
        return Read(offset + 2, length);
    }

    public string String(long offset) => offset == 0 ? "" : Encoding.UTF8.GetString(StringBytes(offset));

    public bool StartsWith(long offset, ReadOnlySpan<byte> expected) =>
        offset >= 0 && offset + expected.Length <= Length && Span.Slice((int)offset, expected.Length).SequenceEqual(expected);

    private ReadOnlySpan<byte> Read(long offset, int size)
    {
        Check(offset, size);
        return _data.Span.Slice((int)offset, size);
    }

    private void Check(long offset, long size)
    {
        if (offset < 0 || size < 0 || offset + size > Length)
            throw new BfShaderFormatException($"Read of {size} bytes runs past the end of the {Length}-byte data", offset);
    }
}
