using System.Buffers;
using System.Runtime.InteropServices;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Decompile;

/// <summary>Lowers Switch 2 Ampere shader instructions to the 64-bit Maxwell instructions the translator reads.</summary>
/// <remarks>
/// Calls the native lowering of the Fritchii emulator through <c>bfs_ampere_lower</c>, a DLL built by
/// tools/AmpereLowerOracle and found next to the application or at the path in BFS_AMPERE_LOWER. The output has no
/// scheduling words. A program the lowering rejects raises <see cref="BfShaderUnsupportedException"/> with its reason.
/// The output buffer starts at the size of the source and doubles when the lowered program is larger.
/// </remarks>
public static unsafe class AmpereLowering
{
    public const string LibraryName = "bfs_ampere_lower";
    public const string PathVariable = "BFS_AMPERE_LOWER";
    private const int BufferTooSmall = 2;

    static AmpereLowering()
    {
        NativeLibrary.SetDllImportResolver(typeof(AmpereLowering).Assembly, (name, _, _) =>
            name == LibraryName && Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } path ? NativeLibrary.Load(path) : IntPtr.Zero);
    }

    public static bool IsAvailable
    {
        get
        {
            try
            {
                return Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } path
                    ? File.Exists(path)
                    : NativeLibrary.TryLoad(LibraryName, typeof(AmpereLowering).Assembly, null, out _);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                return false;
            }
        }
    }

    [DllImport(LibraryName, EntryPoint = "bfs_lower", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Lower(byte* code, uint size, int geometry, int tensor, int tessellation, ulong address,
        ulong* output, uint capacity, uint* count, uint* errorOffset, byte* error, uint errorCapacity);

    public static ulong[] Lower(ReadOnlySpan<byte> code, bool geometry, int tessellation)
    {
        byte[] error = new byte[256];
        for (int capacity = Math.Max(1024, code.Length / 16 * 8); ; capacity *= 2)
        {
            ulong[] output = ArrayPool<ulong>.Shared.Rent(capacity);
            try
            {
                int status = Run(code, geometry, tessellation, output, error, out int count, out uint errorOffset);
                if (status == 0) return output.AsSpan(0, count).ToArray();
                if (status != BufferTooSmall)
                    throw new BfShaderUnsupportedException($"Ampere lowering rejected the program: {System.Text.Encoding.ASCII.GetString(error).TrimEnd('\0')}", errorOffset);
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
