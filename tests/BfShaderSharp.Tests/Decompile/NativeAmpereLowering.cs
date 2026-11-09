using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace BfShaderSharp.Tests.Decompile;

/// <summary>What the native lowering returned for a program: its Maxwell words, or why it refused.</summary>
/// <remarks>A refusal has no words, a reason and the byte offset of the source instruction.</remarks>
internal sealed record NativeLowering(ulong[] Words, string? Error, uint ErrorOffset);

/// <summary>The native Ampere lowering of the Fritchii emulator, used only as the reference the managed lowering is checked against.</summary>
/// <remarks>
/// Calls <c>bfs_lower</c> in the <c>bfs_ampere_lower</c> DLL built by tools/AmpereLowerOracle, found next to the tests or
/// at the path in BFS_AMPERE_LOWER. Tests that need it skip themselves when it is missing. The output buffer starts at
/// the size of the source and doubles when the lowered program is larger.
/// </remarks>
internal static unsafe class NativeAmpereLowering
{
    public const string LibraryName = "bfs_ampere_lower";
    public const string PathVariable = "BFS_AMPERE_LOWER";
    private const int BufferTooSmall = 2;

    static NativeAmpereLowering()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeAmpereLowering).Assembly, (name, _, _) =>
            name == LibraryName && Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } path ? NativeLibrary.Load(path) : IntPtr.Zero);
    }

    public static bool IsAvailable =>
        Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } path && File.Exists(path);

    [DllImport(LibraryName, EntryPoint = "bfs_lower", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Lower(byte* code, uint size, int geometry, int tensor, int tessellation, ulong address,
        ulong* output, uint capacity, uint* count, uint* errorOffset, byte* error, uint errorCapacity);

    public static NativeLowering Lower(ReadOnlySpan<byte> code, bool geometry, int tessellation)
    {
        byte[] error = new byte[256];
        for (int capacity = Math.Max(1024, code.Length / 16 * 8); ; capacity *= 2)
        {
            ulong[] output = ArrayPool<ulong>.Shared.Rent(capacity);
            try
            {
                int status = Run(code, geometry, tessellation, output, error, out int count, out uint errorOffset);
                if (status == 0) return new NativeLowering(output.AsSpan(0, count).ToArray(), null, 0);
                if (status != BufferTooSmall) return new NativeLowering([], Encoding.ASCII.GetString(error).TrimEnd('\0'), errorOffset);
            }
            finally
            {
                ArrayPool<ulong>.Shared.Return(output);
            }
        }
    }

    private static int Run(ReadOnlySpan<byte> code, bool geometry, int tessellation, ulong[] output, byte[] error, out int count, out uint errorOffset)
    {
        uint written, failedAt;
        int status;
        fixed (byte* c = code)
        fixed (ulong* o = output)
        fixed (byte* e = error)
            status = Lower(c, (uint)code.Length, geometry ? 1 : 0, 1, tessellation, 0, o, (uint)output.Length, &written, &failedAt, e, (uint)error.Length);
        count = (int)written;
        errorOffset = failedAt;
        return status;
    }
}
