using BfShaderSharp.Binary;

namespace BfShaderSharp.Tests.Binary;

public class ByteSinkTests
{
    [Fact]
    public void WritesLittleEndianValuesInOrder()
    {
        ByteSink sink = new();
        sink.U8(1);
        sink.U16(0x0302);
        sink.U32(0x07060504);
        sink.U64(0x0F0E0D0C0B0A0908);
        Assert.Equal(15, sink.Length);
        Assert.Equal(Enumerable.Range(1, 15).Select(i => (byte)i), sink.Written.ToArray());
    }

    [Fact]
    public void AlignsWithZeroPaddingAndPatches()
    {
        ByteSink sink = new();
        sink.U8(9);
        sink.Align(8);
        Assert.Equal(8, sink.Length);
        sink.U32(0);
        sink.PatchU32(8, 0xAABBCCDD);
        Assert.Equal(0xAABBCCDDu, new ByteView(sink.Written.ToArray()).U32(8));
        Assert.Equal(0, sink.Written[1]);
    }

    [Fact]
    public void GrowsPastItsFirstBuffer()
    {
        ByteSink sink = new();
        sink.Bytes(new byte[200_000]);
        sink.U32(7);
        Assert.Equal(200_004, sink.Length);
        Assert.Equal(7u, new ByteView(sink.Written.ToArray()).U32(200_000));
    }
}
