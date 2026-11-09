namespace BfShaderSharp.Decompile;

/// <summary>Assembles the byte image the Maxwell translator reads: the 0x50-byte program header, then instructions grouped under scheduling words.</summary>
/// <remarks>
/// The lowering bakes branch distances for a layout whose scheduling words sit at every 32 bytes of the whole image,
/// after the first two instructions. The translator puts its scheduling words 16 bytes earlier, so one NOP
/// precedes the first instruction; every distance is then the same in both layouts. The translator ignores the
/// content of scheduling words, so they are zero.
/// </remarks>
public static class MaxwellProgram
{
    public const int HeaderSize = 0x50;
    private const ulong Nop = 0x50B0000000070F00;

    public static byte[] Build(ReadOnlySpan<byte> header, IReadOnlyList<ulong> instructions)
    {
        int words = instructions.Count + 2 + (instructions.Count + 1) / 3 + 1;
        byte[] image = new byte[HeaderSize + (words + 3) / 4 * 32];
        header[..HeaderSize].CopyTo(image);
        BitConverter.TryWriteBytes(image.AsSpan(HeaderSize + 8), Nop);
        for (int i = 0; i < instructions.Count; i++)
            BitConverter.TryWriteBytes(image.AsSpan(HeaderSize + (i + 2 + (i + 1) / 3) * 8), instructions[i]);
        return image;
    }
}
