using System.Buffers.Binary;

namespace BfShaderSharp.Binary;

/// <summary>A growable little-endian byte buffer that tables are appended to.</summary>
/// <remarks>Positions are absolute offsets from the start of the buffer. Sections are aligned with <see cref="Align"/> so a reader can cast them in place.</remarks>
public sealed class ByteSink
{
    private byte[] _buffer = new byte[1 << 16];

    public long Length { get; private set; }

    public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, (int)Length);

    public void Align(int alignment)
    {
        long padded = (Length + alignment - 1) / alignment * alignment;
        Reserve((int)(padded - Length));
        Length = padded;
    }

    public void U8(byte value) => Reserve(1)[0] = value;

    public void U16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), value);

    public void U32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);

    public void I32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);

    public void U64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), value);

    public void Bytes(ReadOnlySpan<byte> bytes) => bytes.CopyTo(Reserve(bytes.Length));

    public void PatchU32(long offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan((int)offset), value);

    public void PatchU64(long offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan((int)offset), value);

    // Returns a zeroed window of the buffer at the end and moves the end past it.
    private Span<byte> Reserve(int size)
    {
        if (Length + size > _buffer.Length) Array.Resize(ref _buffer, (int)Math.Max(_buffer.Length * 2L, Length + size));
        Span<byte> window = _buffer.AsSpan((int)Length, size);
        window.Clear();
        Length += size;
        return window;
    }
}
