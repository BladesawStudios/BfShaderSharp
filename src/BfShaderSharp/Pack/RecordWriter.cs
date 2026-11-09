using System.Text;
using BfShaderSharp.Binary;

namespace BfShaderSharp.Pack;

/// <summary>Appends the fields of a record to a buffer in the order a <see cref="RecordReader"/> reads them.</summary>
/// <remarks>Lists are a 32-bit count followed by their items, strings a 16-bit length and UTF-8 characters, and byte runs a 32-bit length and the bytes. All values are little-endian.</remarks>
public sealed class RecordWriter
{
    private readonly ByteSink _sink = new();

    public void U8(byte value) => _sink.U8(value);

    public void U16(ushort value) => _sink.U16(value);

    public void U32(uint value) => _sink.U32(value);

    public void I32(int value) => _sink.I32(value);

    public void F32(float value) => _sink.U32(BitConverter.SingleToUInt32Bits(value));

    public void Count(int value) => _sink.U32((uint)value);

    public void String(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        _sink.U16((ushort)bytes.Length);
        _sink.Bytes(bytes);
    }

    public void Key(BlobKey key)
    {
        _sink.U64(key.High);
        _sink.U64(key.Low);
    }

    public void Bytes(ReadOnlySpan<byte> bytes)
    {
        _sink.U32((uint)bytes.Length);
        _sink.Bytes(bytes);
    }

    public byte[] ToArray() => _sink.Written.ToArray();
}
