using BfShaderSharp.Binary;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Tests.Binary;

public class ByteViewTests
{
    private static readonly byte[] Data = [1, 0, 2, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 5, 0, (byte)'h', (byte)'i', 0, 0];

    [Fact]
    public void ReadsLittleEndianValues()
    {
        ByteView view = new(Data);
        Assert.Equal(1, view.U8(0));
        Assert.Equal(1, view.U16(0));
        Assert.Equal(2u, view.U32(2));
        Assert.Equal(3ul, view.U64(6));
        Assert.Equal(-1, view.I32(14));
    }

    [Fact]
    public void ReadsLengthPrefixedStrings()
    {
        ByteView view = new(new byte[] { 0, 0, 0, 0, 2, 0, (byte)'h', (byte)'i' });
        Assert.Equal("hi", view.String(4));
        Assert.Equal("", view.String(0));
    }

    [Fact]
    public void ReadsPastTheEndAreFormatErrorsWithTheOffset()
    {
        ByteView view = new(Data);
        BfShaderFormatException error = Assert.Throws<BfShaderFormatException>(() => view.U32(22));
        Assert.Equal(22, error.Offset);
        Assert.Throws<BfShaderFormatException>(() => view.U8(-1));
        Assert.Throws<BfShaderFormatException>(() => view.Slice(10, 100));
        Assert.Throws<BfShaderFormatException>(() => view.String(18));
    }

    [Fact]
    public void CountsAndPositionsThatDoNotFitAreFormatErrors()
    {
        ByteView view = new(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
        Assert.Throws<BfShaderFormatException>(() => view.Count(0));
        Assert.Throws<BfShaderFormatException>(() => view.Position(0));
    }

    [Fact]
    public void SlicesShareTheBytes()
    {
        ByteView view = new(Data);
        Assert.Equal(2u, view.Slice(2, 8).U32(0));
        Assert.True(view.StartsWith(0, [1, 0, 2]));
        Assert.False(view.StartsWith(22, [0, 0, 0]));
    }
}
