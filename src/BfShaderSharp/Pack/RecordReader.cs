using System.Text;
using BfShaderSharp.Binary;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Pack;

/// <summary>Reads the fields of a record one after another from its bytes.</summary>
/// <remarks>The counterpart of <see cref="RecordWriter"/>. Every read is range-checked by the view, so a truncated or corrupt record throws <see cref="BfShaderFormatException"/> with the offset, and a count larger than the bytes left is rejected before anything is allocated for it.</remarks>
public sealed class RecordReader(ReadOnlyMemory<byte> bytes)
{
    private readonly ByteView _view = new(bytes);
    private int _position;

    public byte U8() => _view.U8(_position++);

    public ushort U16() => _view.U16(Advance(2));

    public uint U32() => _view.U32(Advance(4));

    public int I32() => _view.I32(Advance(4));

    public float F32() => BitConverter.UInt32BitsToSingle(U32());

    // A list length, checked against the bytes that remain so a corrupt count cannot ask for more items than could exist.
    public int Count(int minimumItemSize = 1)
    {
        int count = _view.Count(Advance(4));
        if ((long)count * minimumItemSize > _view.Length - _position) throw new BfShaderFormatException($"List of {count} items does not fit the record", _position);
        return count;
    }

    public string String()
    {
        int length = _view.U16(_position);
        string value = Encoding.UTF8.GetString(_view.Bytes(_position + 2, length).Span);
        _position += 2 + length;
        return value;
    }

    public BlobKey Key() => new(_view.U64(Advance(8)), _view.U64(Advance(8)));

    public ReadOnlyMemory<byte> Bytes()
    {
        int length = _view.Count(_position);
        ReadOnlyMemory<byte> value = _view.Bytes(_position + 4, length);
        _position += 4 + length;
        return value;
    }

    // Moves past a field and returns where it starts.
    private int Advance(int size)
    {
        int start = _position;
        _position += size;
        return start;
    }
}
