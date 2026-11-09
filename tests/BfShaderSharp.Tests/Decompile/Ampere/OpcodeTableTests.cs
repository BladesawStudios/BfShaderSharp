using BfShaderSharp.Decompile.Ampere;

namespace BfShaderSharp.Tests.Decompile.Ampere;

public class OpcodeTableTests
{
    [Fact]
    public void RefusesAnOpcodeRegisteredTwice()
    {
        var table = new OpcodeTable();
        table.Add(_ => true, 0x123);
        Assert.Throws<InvalidOperationException>(() => table.Add(_ => true, 0x123));
    }

    [Fact]
    public void FindsARegisteredHandlerAndNothingForAnUnknownOpcode()
    {
        var table = new OpcodeTable();
        Func<InstructionContext, bool> handler = _ => true;
        table.Add(handler, 0x10, 0x20);
        Assert.Same(handler, table.Find(0x20));
        Assert.Null(table.Find(0x30));
    }

    [Theory]
    [InlineData(0x802u)]
    [InlineData(0x94du)]
    [InlineData(0x947u)]
    [InlineData(0xb60u)]
    [InlineData(0x210u)]
    public void TheSharedTableCoversTheOpcodesTheGamesUse(uint opcode) => Assert.NotNull(OpcodeTable.Instance.Find(opcode));
}
