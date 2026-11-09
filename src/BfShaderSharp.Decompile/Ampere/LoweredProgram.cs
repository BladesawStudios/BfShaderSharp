namespace BfShaderSharp.Decompile.Ampere;

/// <summary>The result of lowering one program: Maxwell instructions, or the reason it could not be lowered.</summary>
/// <remarks>
/// A successful result lists the 64-bit instructions without scheduling words, the output position of every source
/// instruction, and the texture constant buffer the lowered texture instructions need. A failed result has an empty
/// instruction list, the byte offset of the offending source instruction and a reason. <see cref="HighUniformRegisters"/>
/// reports how many registers a second attempt must reserve when the program uses uniform registers above UR27.
/// </remarks>
public sealed class LoweredProgram
{
    public List<ulong> Instructions { get; } = [];

    public List<uint> SourceInstructionOffsets { get; } = [];

    public uint SourceSize { get; set; }

    public uint RequiredTextureBuffer { get; set; } = uint.MaxValue;

    public uint HighUniformRegisters { get; set; }

    public uint ErrorOffset { get; private set; }

    public string? Error { get; private set; }

    public bool Succeeded => Error is null;

    public void Fail(uint instructionIndex, string reason)
    {
        Instructions.Clear();
        SourceInstructionOffsets.Clear();
        ErrorOffset = instructionIndex * 16;
        Error = reason;
    }
}
