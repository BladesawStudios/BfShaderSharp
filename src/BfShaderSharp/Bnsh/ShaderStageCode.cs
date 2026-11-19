using BfShaderSharp.Binary;

namespace BfShaderSharp.Bnsh;

/// <summary>The compiled code of one stage: the GPU byte code and the control code that accompanies it.</summary>
/// <remarks>Both are slices of the file bytes and nothing is copied. The control code locates the immediate constants that follow the byte code. The code target is the BNSH's: zero for Switch (Maxwell code), non-zero for Switch 2 (Ampere code).</remarks>
public readonly record struct ShaderStageCode(ShaderStage Stage, ReadOnlyMemory<byte> ByteCode, ReadOnlyMemory<byte> ControlCode, uint CodeTarget = 0)
{
    /// <summary>True when the code is Switch 2's Ampere code, which a Maxwell translator needs lowered first.</summary>
    public bool IsAmpere => CodeTarget != 0;

    internal static ShaderStageCode Read(ByteView bnsh, ShaderStage stage, long offset, uint codeTarget)
    {
        ulong controlOffset = bnsh.U64(offset + 8);
        ulong byteOffset = bnsh.U64(offset + 0x10);
        uint byteSize = bnsh.U32(offset + 0x18);
        uint controlSize = bnsh.U32(offset + 0x1C);
        return new ShaderStageCode(stage, bnsh.Bytes((long)byteOffset, byteSize), bnsh.Bytes((long)controlOffset, controlSize), codeTarget);
    }
}
