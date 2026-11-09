using BfShaderSharp.Decompile.Ampere;

namespace BfShaderSharp.Tests.Decompile.Ampere;

public class LoweringStateTests
{
    private static LoweringState State(params AmpereInstruction[] code) => new(code, 0, new LoweringOptions());

    [Theory]
    [InlineData(0u, 0x50u)]
    [InlineData(1u, 0x58u)]
    [InlineData(2u, 0x68u)]
    [InlineData(3u, 0x70u)]
    public void InstructionsSitWhereSchedulingWordsPutThem(uint index, uint address) => Assert.Equal(address, LoweringState.AddressOf(index));

    [Fact]
    public void FailRecordsTheReasonAndEndsTheLowering()
    {
        LoweringState state = State(Asm.Exit());
        Assert.False(state.Fail(0, "why"));
        Assert.True(state.Done);
        Assert.Equal("why", state.Result.Error);
    }

    [Fact]
    public void FinishPatchesBranchDistancesAndSizesTheProgram()
    {
        LoweringState state = State(Asm.Exit(), Asm.Exit(), Asm.Exit(), Asm.Exit());
        state.Result.Instructions.Add(0xe24000000000000f);
        state.Result.SourceInstructionOffsets.AddRange([0u, 1u, 2u, 3u]);
        state.Relocations.Add(new Relocation(0, 3));
        Assert.True(state.Finish(3));
        // Instruction 3 sits 0x20 bytes after instruction 0, less the branch's own 8.
        Assert.Equal(0xe24000000000000fUL | (24UL << 20), state.Result.Instructions[0]);
        Assert.Equal(64u, state.Result.SourceSize);
    }

    [Fact]
    public void FinishRefusesABranchPastTheProgram()
    {
        LoweringState state = State(Asm.Exit());
        state.Result.Instructions.Add(0xe24000000000000f);
        state.Result.SourceInstructionOffsets.Add(0);
        state.Relocations.Add(new Relocation(0, 5));
        Assert.False(state.Finish(0));
        Assert.Equal("native branch or call leaves captured program", state.Result.Error);
    }

    [Fact]
    public void HighUniformsMoveTheirBaseDownFromR216()
    {
        Assert.Equal(216u, State().HighUniformBase);
        Assert.Equal(212u, new LoweringState([], 4, new LoweringOptions()).HighUniformBase);
    }
}
