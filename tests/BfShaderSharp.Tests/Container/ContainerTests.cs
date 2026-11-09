using System.Text;
using BfShaderSharp.Binary;
using BfShaderSharp.Container;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Tests.Container;

public class ContainerTests
{
    private static byte[] Header(string magic, ushort order = 0xFEFF)
    {
        byte[] bytes = new byte[0x40];
        Encoding.ASCII.GetBytes(magic).CopyTo(bytes, 0);
        bytes[8] = 1;
        bytes[9] = 4;
        BitConverter.TryWriteBytes(bytes.AsSpan(0x0A), (ushort)2);
        BitConverter.TryWriteBytes(bytes.AsSpan(0x0C), order);
        bytes[0x0E] = 12;
        BitConverter.TryWriteBytes(bytes.AsSpan(0x16), (ushort)0x60);
        return bytes;
    }

    [Fact]
    public void ReadsTheSharedHeader()
    {
        ResHeader header = ResHeader.Read(new ByteView(Header("BNSH")), "BNSH");
        Assert.Equal(new Version(2, 4, 1), header.Version);
        Assert.Equal(12, header.AlignmentShift);
        Assert.Equal(0x60, header.BlockOffset);
    }

    [Fact]
    public void RejectsTheWrongMagicAShortFileAndBigEndian()
    {
        Assert.Throws<BfShaderFormatException>(() => ResHeader.Read(new ByteView(Header("FSHA")), "BNSH"));
        Assert.Throws<BfShaderFormatException>(() => ResHeader.Read(new ByteView(new byte[8]), "BNSH"));
        Assert.Throws<BfShaderUnsupportedException>(() => ResHeader.Read(new ByteView(Header("BNSH", 0xFFFE)), "BNSH"));
    }

    // A dictionary of two keys: a header, a root node, then one node for each key that points at a length-prefixed string.
    private static byte[] Dictionary(params string[] keys)
    {
        ByteSink sink = new();
        sink.U64(0);
        sink.U32(0x5F444943);
        sink.I32(keys.Length);
        long strings = 16 + (keys.Length + 1) * 16;
        sink.U64(0);
        sink.U64(0);
        long at = strings;
        foreach (string key in keys)
        {
            sink.U64(0);
            sink.U64((ulong)at);
            at += 2 + Encoding.UTF8.GetByteCount(key);
        }

        foreach (string key in keys)
        {
            sink.U16((ushort)Encoding.UTF8.GetByteCount(key));
            sink.Bytes(Encoding.UTF8.GetBytes(key));
        }

        return sink.Written.ToArray();
    }

    [Fact]
    public void ReadsKeysAndFindsThemByName()
    {
        ResDict dict = new(new ByteView(Dictionary("alpha", "beta", "gamma")), 8);
        Assert.Equal(3, dict.Count);
        Assert.Equal("beta", dict.Key(1));
        Assert.Equal(2, dict.IndexOf("gamma"));
        Assert.Equal(-1, dict.IndexOf("delta"));
        Assert.Equal(["alpha", "beta", "gamma"], dict.Keys());
    }

    [Fact]
    public void AnOffsetOfZeroIsAnEmptyDictionaryAndABadCountIsAnError()
    {
        Assert.Equal(0, new ResDict(new ByteView(new byte[16]), 0).Count);
        byte[] bad = Dictionary("a");
        BitConverter.TryWriteBytes(bad.AsSpan(12), 1_000_000);
        Assert.Throws<BfShaderFormatException>(() => new ResDict(new ByteView(bad), 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResDict(new ByteView(Dictionary("a")), 8).Key(5));
    }
}
