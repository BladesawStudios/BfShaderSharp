using System.Runtime.InteropServices;
using Ryujinx.Graphics.Shader;

namespace BfShaderSharp.Decompile;

/// <summary>Serves the translator the program image and answers its other questions with the translator's defaults.</summary>
/// <remarks>Constant buffer 1 reads return zero unless immediate constants were supplied.</remarks>
internal sealed class GpuAccessor(byte[] image, byte[] constants, (int X, int Y, int Z) workgroup, MeshProgramInfo? mesh = null) : IGpuAccessor
{
    public MeshProgramInfo QueryMeshProgram() => mesh!;

    public int QueryComputeSharedMemorySize() => mesh?.SharedMemoryBytes ?? 0xc000;

    public int QueryComputeLocalSizeX() => workgroup.X;

    public int QueryComputeLocalSizeY() => workgroup.Y;

    public int QueryComputeLocalSizeZ() => workgroup.Z;

    public uint ConstantBuffer1Read(int offset) =>
        offset >= 0 && offset + 4 <= constants.Length ? BitConverter.ToUInt32(constants, offset) : 0;

    // Binding sets follow the translator's own tool: constant buffers from binding 1, storage buffers, textures, images.
    private const int DefaultArrayLength = 32;
    private int _textures, _images;

    public SetBindingPair CreateConstantBufferBinding(int index) => new(0, index + 1);

    public SetBindingPair CreateStorageBufferBinding(int index) => new(1, index);

    public SetBindingPair CreateTextureBinding(int count, bool isBuffer)
    {
        int binding = _textures;
        _textures += count;
        return new SetBindingPair(2, binding);
    }

    public SetBindingPair CreateImageBinding(int count, bool isBuffer)
    {
        int binding = _images;
        _images += count;
        return new SetBindingPair(3, binding);
    }

    public int QuerySamplerArrayLengthFromPool() => DefaultArrayLength;

    public int QueryTextureArrayLengthFromBuffer(int slot) => DefaultArrayLength;

    public int QueryTextureArrayLengthFromPool() => DefaultArrayLength;

    public ReadOnlySpan<ulong> GetCode(ulong address, int minimumSize) =>
        MemoryMarshal.Cast<byte, ulong>(new ReadOnlySpan<byte>(image).Slice((int)address));
}
