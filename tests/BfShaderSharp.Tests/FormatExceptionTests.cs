using BfShaderSharp.Errors;

namespace BfShaderSharp.Tests;

public class FormatExceptionTests
{
    [Fact]
    public void MessageCarriesTheOffset()
    {
        BfShaderFormatException error = new("bad magic", 0x20);
        Assert.Equal(0x20, error.Offset);
        Assert.Contains("0x20", error.Message);
    }
}
