using System.Runtime.InteropServices;
using Ryujinx.Graphics.Shader;

namespace BfShaderSharp.Decompile;

/// <summary>Serves the translator the program image and answers its other questions with the translator's defaults.</summary>
/// <remarks>Constant buffer 1 reads return zero unless immediate constants were supplied.</remarks>
internal sealed class GpuAccessor(byte[] image, byte[] constants) : IGpuAccessor
{
    public uint ConstantBuffer1Read(int offset) =>
        offset >= 0 && offset + 4 <= constants.Length ? BitConverter.ToUInt32(constants, offset) : 0;

    public ReadOnlySpan<ulong> GetCode(ulong address, int minimumSize) =>
        MemoryMarshal.Cast<byte, ulong>(new ReadOnlySpan<byte>(image).Slice((int)address));
}
