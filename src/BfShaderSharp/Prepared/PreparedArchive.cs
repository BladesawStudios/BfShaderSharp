using System.IO.Compression;
using System.Text;
using BfShaderSharp.Binary;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Prepared;

/// <summary>A prepared file: shading models with their programs, bindings and decompiled GLSL.</summary>
/// <remarks>
/// Reading checks the header and the table positions and nothing else; models, strings and blobs are read when asked
/// for. A blob is decompressed on request, and the file bytes are never copied, so a memory-mapped file costs only the
/// pages that are touched.
/// </remarks>
public sealed class PreparedArchive
{
    private readonly ByteView _view;
    private readonly long _modelTable;
    private readonly long _stringTable;
    private readonly long _blobTable;

    private PreparedArchive(ByteView view)
    {
        _view = view;
        ModelCount = checked((int)view.U32(8));
        _modelTable = Position(view, 0x10, (long)ModelCount * 8);
        _stringTable = Position(view, 0x18, 8);
        _blobTable = Position(view, 0x20, 8);
        StringCount = checked((int)view.U32(_stringTable));
        BlobCount = checked((int)view.U32(_blobTable));
        Position(view, 0x18, 4 + (long)(StringCount + 1) * 4);
        Position(view, 0x20, 8 + (long)BlobCount * PreparedLayout.BlobEntrySize);
    }

    public int ModelCount { get; }

    public int StringCount { get; }

    public int BlobCount { get; }

    public static PreparedArchive Read(ReadOnlyMemory<byte> bytes)
    {
        ByteView view = new(bytes);
        if (view.Length < PreparedLayout.HeaderSize || view.U32(0) != PreparedLayout.Magic) throw new BfShaderFormatException("Not a prepared shader file", 0);
        if (view.U32(4) != PreparedLayout.Version) throw new BfShaderUnsupportedException($"Prepared file version {view.U32(4)} is not supported", 4);
        if (view.U64(0x30) != (ulong)view.Length) throw new BfShaderFormatException("Prepared file size does not match its header", 0x30);
        return new PreparedArchive(view);
    }

    public PreparedModel Model(int index)
    {
        if ((uint)index >= (uint)ModelCount) throw new ArgumentOutOfRangeException(nameof(index));
        return new PreparedModel(this, _view, (long)_view.U64(_modelTable + (long)index * 8));
    }

    public PreparedModel? Model(string name)
    {
        for (int i = 0; i < ModelCount; i++)
        {
            PreparedModel model = Model(i);
            if (model.Name == name) return model;
        }

        return null;
    }

    public IEnumerable<PreparedModel> Models()
    {
        for (int i = 0; i < ModelCount; i++) yield return Model(i);
    }

    public string String(int index)
    {
        if ((uint)index >= (uint)StringCount) throw new BfShaderFormatException($"String index {index} is out of range", _stringTable);
        long offsets = _stringTable + 4;
        long data = offsets + (long)(StringCount + 1) * 4;
        uint start = _view.U32(offsets + (long)index * 4);
        uint end = _view.U32(offsets + (long)(index + 1) * 4);
        return Encoding.UTF8.GetString(_view.Bytes(data + start, end - start).Span);
    }

    /// <summary>Decompresses a blob: the GLSL of a stage or the default contents of a uniform block.</summary>
    public byte[] Blob(int index)
    {
        if ((uint)index >= (uint)BlobCount) throw new BfShaderFormatException($"Blob index {index} is out of range", _blobTable);
        long entry = _blobTable + 8 + (long)index * PreparedLayout.BlobEntrySize;
        ReadOnlyMemory<byte> compressed = _view.Bytes((long)_view.U64(entry), _view.U32(entry + 8));
        byte[] raw = new byte[_view.U32(entry + 12)];
        if (!BrotliDecoder.TryDecompress(compressed.Span, raw, out int written) || written != raw.Length)
            throw new BfShaderFormatException($"Blob {index} does not decompress", (long)_view.U64(entry));
        return raw;
    }

    public string Glsl(int blob) => Encoding.UTF8.GetString(Blob(blob));

    // Checks that a table the header points at fits in the file, and returns its position.
    private static long Position(ByteView view, int field, long size)
    {
        long at = checked((long)view.U64(field));
        if (at < PreparedLayout.HeaderSize || at + size > view.Length) throw new BfShaderFormatException($"Table of {size} bytes runs past the file", at);
        return at;
    }
}
