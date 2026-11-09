using BfShaderSharp.Decompile.Ampere;

namespace BfShaderSharp.Tests.Decompile.Ampere;

/// <summary>Builds Ampere instructions and programs for the lowering tests.</summary>
/// <remarks>
/// Word 0 holds the opcode in bits 0-11, the guard predicate in 12-15, the destination register in 16-23 and the first
/// source register in 24-31; words 1 to 3 are the rest of the encoding. The guard 7 is the always-true predicate.
/// </remarks>
internal static class Asm
{
    public static AmpereInstruction Insn(uint opcode, uint guard = 7, uint dest = 255, uint a = 255, uint w1 = 0, uint w2 = 0, uint w3 = 0) =>
        new() { W0 = opcode | (guard << 12) | (dest << 16) | (a << 24), W1 = w1, W2 = w2, W3 = w3 };

    public static AmpereInstruction Exit(uint guard = 7) => Insn(0x94d, guard, w2: 0x03800000);

    public static byte[] Program(params AmpereInstruction[] instructions)
    {
        byte[] bytes = new byte[instructions.Length * 16];
        for (int i = 0; i < instructions.Length; i++)
        {
            for (int word = 0; word < 4; word++) BitConverter.TryWriteBytes(bytes.AsSpan(i * 16 + word * 4), instructions[i][word]);
        }

        return bytes;
    }

    public static LoweredProgram Lower(params AmpereInstruction[] instructions) =>
        AmpereLowerer.Lower(Program(instructions), new LoweringOptions(Tensor: true));
}
