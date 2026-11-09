using BfShaderSharp.Decompile.Ampere;

namespace BfShaderSharp.Tests.Decompile.Ampere;

public class AmpereInstructionTests
{
    [Fact]
    public void ReadsItsFourWordsLittleEndian()
    {
        byte[] bytes = [1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0, 0, 0];
        var instruction = new AmpereInstruction(bytes);
        Assert.Equal([1u, 2u, 3u, 4u], [instruction.W0, instruction.W1, instruction.W2, instruction.W3]);
        Assert.Equal(3u, instruction[2]);
    }

    [Fact]
    public void ExtractsFieldsWithinAndAcrossWords()
    {
        var instruction = new AmpereInstruction { W0 = 0xabcd1234, W1 = 0x000000f5 };
        Assert.Equal(0x234u, instruction.Opcode);
        Assert.Equal(0xcdu, instruction.Bits(16, 8));
        // Bits 28-35: the top nibble of word 0 (0xa) and the low nibble of word 1 (0x5).
        Assert.Equal(0x5au, instruction.Bits(28, 8));
    }

    [Fact]
    public void WritesWordsByIndex()
    {
        var instruction = new AmpereInstruction();
        for (int word = 0; word < 4; word++) instruction[word] = (uint)(word + 10);
        Assert.Equal([10u, 11u, 12u, 13u], [instruction.W0, instruction.W1, instruction.W2, instruction.W3]);
    }
}
