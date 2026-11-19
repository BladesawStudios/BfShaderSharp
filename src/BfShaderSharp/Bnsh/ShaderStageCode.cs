using BfShaderSharp.Binary;

namespace BfShaderSharp.Bnsh;

/// <summary>The GPU instruction set a stage's byte code is written in.</summary>
public enum ShaderArchitecture
{
    /// <summary>Switch 2 code, which a Maxwell translator needs lowered first.</summary>
    Ampere,

    /// <summary>Switch code, which the translator reads as it is.</summary>
    Maxwell,
}

/// <summary>The compiled code of one stage: the GPU byte code and the control code that accompanies it.</summary>
/// <remarks>Both are slices of the file bytes and nothing is copied. The control code locates the immediate constants that follow the byte code. A BNSH whose code target is zero holds Switch (Maxwell) code; any other holds Switch 2 (Ampere) code, which is what a stage built without a file is taken to be.</remarks>
public readonly record struct ShaderStageCode(ShaderStage Stage, ReadOnlyMemory<byte> ByteCode, ReadOnlyMemory<byte> ControlCode, ShaderArchitecture Architecture = ShaderArchitecture.Ampere)
{
    internal static ShaderStageCode Read(ByteView bnsh, ShaderStage stage, long offset, uint codeTarget)
    {
        ulong controlOffset = bnsh.U64(offset + 8);
        ulong byteOffset = bnsh.U64(offset + 0x10);
        uint byteSize = bnsh.U32(offset + 0x18);
        uint controlSize = bnsh.U32(offset + 0x1C);
        return new ShaderStageCode(stage, bnsh.Bytes((long)byteOffset, byteSize), bnsh.Bytes((long)controlOffset, controlSize), codeTarget == 0 ? ShaderArchitecture.Maxwell : ShaderArchitecture.Ampere);
    }
}
