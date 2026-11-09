using BfShaderSharp.Errors;
using BfShaderSharp.Pack;

namespace BfShaderSharp.Tests.Pack;

public class RecordIoTests
{
    [Fact]
    public void FieldsComeBackInTheOrderTheyWereWritten()
    {
        RecordWriter writer = new();
        BlobKey key = new(1, 2);
        writer.U8(7);
        writer.U16(500);
        writer.U32(70000);
        writer.I32(-5);
        writer.F32(1.5f);
        writer.String("héllo");
        writer.Key(key);
        writer.Bytes([4, 5, 6]);
        RecordReader reader = new(writer.ToArray());
        Assert.Equal(7, reader.U8());
        Assert.Equal(500, reader.U16());
        Assert.Equal(70000u, reader.U32());
        Assert.Equal(-5, reader.I32());
        Assert.Equal(1.5f, reader.F32());
        Assert.Equal("héllo", reader.String());
        Assert.Equal(key, reader.Key());
        Assert.Equal(new byte[] { 4, 5, 6 }, reader.Bytes().ToArray());
    }

    [Fact]
    public void ATruncatedRecordIsAFormatError()
    {
        RecordWriter writer = new();
        writer.U32(1);
        RecordReader reader = new(writer.ToArray());
        reader.U32();
        Assert.Throws<BfShaderFormatException>(() => reader.U32());
    }

    [Fact]
    public void ACountLargerThanTheBytesLeftIsRejectedBeforeAnythingIsAllocated()
    {
        RecordWriter writer = new();
        writer.Count(int.MaxValue);
        Assert.Throws<BfShaderFormatException>(() => new RecordReader(writer.ToArray()).Count(4));
    }

    [Fact]
    public void KeysOrderByTheirHalvesAndPrintAsHex()
    {
        Assert.True(new BlobKey(1, 9).CompareTo(new BlobKey(2, 0)) < 0);
        Assert.Equal("00000000000000010000000000000002", new BlobKey(1, 2).ToString());
        Assert.Equal(BlobKey.Of([1, 2, 3]), BlobKey.Of([1, 2, 3]));
        Assert.NotEqual(BlobKey.Of([1, 2, 3]), BlobKey.Of([1, 2, 4]));
    }
}
