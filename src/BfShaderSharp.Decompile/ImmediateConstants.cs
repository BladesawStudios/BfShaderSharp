using System.Buffers.Binary;

namespace BfShaderSharp.Decompile;

/// <summary>Reads the immediate constants of a Switch stage: the bytes its control block locates after the byte code, which a program that branches through a jump table reads its targets from.</summary>
/// <remarks>
/// The control block holds the constant buffer's size at 0x6FC and its offset into the stage's byte code at 0x700. A block that is too short, or a range that
/// runs past the byte code, gives no constants, and a program that reads them then reads zeros.
/// </remarks>
internal static class ImmediateConstants
{
    private const int SizeAt = 0x6FC;
    private const int OffsetAt = 0x700;

    public static byte[] Read(ReadOnlySpan<byte> control, ReadOnlySpan<byte> byteCode)
    {
        if (control.Length < OffsetAt + 4) return [];
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(control[SizeAt..]);
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(control[OffsetAt..]);
        if (size == 0 || (ulong)offset + size > (ulong)byteCode.Length) return [];
        return byteCode.Slice((int)offset, (int)size).ToArray();
    }
}
