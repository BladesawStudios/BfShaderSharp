using System.Text;
using BfShaderSharp.Assets;

namespace BfShaderSharp.Tests.Assets;

public class UnifyTexTests
{
    // A BYML v7 file holding { "Old": "New" }: header, key table, string table, then the root dictionary.
    private static byte[] Byml()
    {
        List<byte> bytes = [(byte)'Y', (byte)'B', 7, 0];
        void Int(int value) => bytes.AddRange(BitConverter.GetBytes(value));
        byte[] Table(string text)
        {
            byte[] chars = [.. Encoding.UTF8.GetBytes(text), 0];
            List<byte> table = [0xC2, 1, 0, 0];
            table.AddRange(BitConverter.GetBytes(12));
            table.AddRange(BitConverter.GetBytes(12 + chars.Length));
            table.AddRange(chars);
            while (table.Count % 4 != 0) table.Add(0);
            return [.. table];
        }

        byte[] keys = Table("Old");
        byte[] values = Table("New");
        Int(16);
        Int(16 + keys.Length);
        Int(16 + keys.Length + values.Length);
        bytes.AddRange(keys);
        bytes.AddRange(values);
        bytes.AddRange([0xC1, 1, 0, 0, 0, 0, 0, 0xA0]);
        Int(0);
        return [.. bytes];
    }

    [Fact]
    public void ANameMapsToTheTextureThatStandsInForIt()
    {
        UnifyTex table = UnifyTex.Parse(Byml());
        Assert.Equal("New", table.TargetOf("Old"));
        Assert.Null(table.TargetOf("New"));
        Assert.Equal(1, table.Count);
    }

    [Fact]
    public void ANonBymlFileIsAFormatError() =>
        Assert.Throws<BfShaderSharp.Errors.BfShaderFormatException>(() => UnifyTex.Parse(new byte[32]));
}
