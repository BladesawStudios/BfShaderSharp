using BfShaderSharp.Decompile.Ampere;

namespace BfShaderSharp.Tests.Decompile.Ampere;

public class HandlerRobustnessTests
{
    private const int Seed = 20260511;
    private const int InstructionsPerOpcode = 12;

    // A handler may reject any malformed instruction, but must do so by result, never by throwing.
    [Fact]
    public void NoOpcodeThrowsOnMalformedOperands()
    {
        var random = new Random(Seed);
        List<string> thrown = [];
        for (uint opcode = 0; opcode < 0x1000; opcode++)
        {
            if (OpcodeTable.Instance.Find(opcode) is null) continue;
            for (int attempt = 0; attempt < InstructionsPerOpcode; attempt++)
            {
                AmpereInstruction insn = Asm.Insn(opcode, guard: (uint)random.Next(16), dest: (uint)random.Next(256), a: (uint)random.Next(256),
                    w1: (uint)random.NextInt64(), w2: (uint)random.NextInt64(), w3: (uint)random.NextInt64() & 0x3fffe00);
                try
                {
                    Asm.Lower(insn, Asm.Exit());
                }
                catch (Exception ex)
                {
                    thrown.Add($"0x{opcode:x}: {ex.GetType().Name}: {ex.Message} (W {insn.W0:x8} {insn.W1:x8} {insn.W2:x8} {insn.W3:x8})");
                }
            }
        }

        Assert.Empty(thrown);
    }

    [Fact]
    public void AnUnregisteredOpcodeIsRejectedByName()
    {
        LoweredProgram program = Asm.Lower(Asm.Insn(0xfff), Asm.Exit());
        Assert.Equal("unsupported native opcode", program.Error);
    }
}
