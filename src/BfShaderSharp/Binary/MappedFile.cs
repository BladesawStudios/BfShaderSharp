using System.Buffers;
using System.IO.MemoryMappedFiles;

namespace BfShaderSharp.Binary;

/// <summary>A read-only memory-mapped file exposed as memory, so only the pages that are read are loaded.</summary>
/// <remarks>
/// Disposing unmaps the file; memory taken from the manager's memory must not be used afterwards. A file larger than
/// 2 GiB is rejected because <see cref="ReadOnlyMemory{T}"/> cannot address it.
/// </remarks>
public sealed unsafe class MappedFile : MemoryManager<byte>
{
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte* _pointer;
    private readonly int _length;

    private MappedFile(MemoryMappedFile file, MemoryMappedViewAccessor view, byte* pointer, int length)
    {
        _file = file;
        _view = view;
        _pointer = pointer;
        _length = length;
    }

    public static MappedFile Open(string path)
    {
        long length = new FileInfo(path).Length;
        if (length is 0 or > int.MaxValue) throw new IOException($"{path} cannot be mapped: {length} bytes");
        MemoryMappedFile file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        MemoryMappedViewAccessor view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        byte* pointer = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        return new MappedFile(file, view, pointer + view.PointerOffset, (int)length);
    }

    public override Span<byte> GetSpan() => new(_pointer, _length);

    public override MemoryHandle Pin(int elementIndex = 0) => new(_pointer + elementIndex);

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _file.Dispose();
    }
}
