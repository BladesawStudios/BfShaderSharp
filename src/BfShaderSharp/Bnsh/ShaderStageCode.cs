using BfShaderSharp.Binary;

namespace BfShaderSharp.Bnsh;

/// <summary>The compiled code of one stage: the GPU byte code and the control code that accompanies it.</summary>
/// <remarks>Both are slices of the file bytes and nothing is copied. The control code locates the immediate constants that follow the byte code.</remarks>
public readonly record struct ShaderStageCode(ShaderStage Stage, ReadOnlyMemory<byte> ByteCode, ReadOnlyMemory<byte> ControlCode)
{
    internal static ShaderStageCode Read(ByteView bnsh, ShaderStage stage, long offset)
    {
        ulong controlOffset = bnsh.U64(offset + 8);
        ulong byteOffset = bnsh.U64(offset + 0x10);
        uint byteSize = bnsh.U32(offset + 0x18);
        uint controlSize = bnsh.U32(offset + 0x1C);
        return new ShaderStageCode(stage, bnsh.Bytes((long)byteOffset, byteSize), bnsh.Bytes((long)controlOffset, controlSize));
    }
}
