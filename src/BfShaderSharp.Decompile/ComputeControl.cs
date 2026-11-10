using System.Buffers.Binary;

namespace BfShaderSharp.Decompile;

/// <summary>Reads the thread-group size of a compute stage from the control block that comes with its code.</summary>
/// <remarks>
/// A compiled shader file's control block starts with the magic 0x98761234 and holds the three group dimensions in the
/// words at 0x58, 0x5c and 0x60. A control block that is absent or does not start with the magic gives a group of one thread.
/// </remarks>
internal static class ComputeControl
{
    private const uint Magic = 0x98761234;
    private const int First = 0x58;

    public static (int X, int Y, int Z) WorkgroupSize(ReadOnlySpan<byte> control)
    {
        if (control.Length < First + 12 || BinaryPrimitives.ReadUInt32LittleEndian(control) != Magic) return (1, 1, 1);
        return (Dimension(control, First), Dimension(control, First + 4), Dimension(control, First + 8));
    }

    private static int Dimension(ReadOnlySpan<byte> control, int at) => (int)Math.Clamp(BinaryPrimitives.ReadUInt32LittleEndian(control[at..]), 1U, 1024U);
}
