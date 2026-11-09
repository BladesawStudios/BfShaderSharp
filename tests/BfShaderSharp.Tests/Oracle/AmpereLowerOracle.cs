using System.Runtime.InteropServices;

namespace BfShaderSharp.Tests.Oracle;

/// <summary>The native Ampere-to-Maxwell lowering of the Fritchii emulator, loaded from the DLL that tools/AmpereLowerOracle builds.</summary>
/// <remarks>BFS_ORACLE names the DLL. Tests compare the managed lowering against it; without the variable the oracle tests skip themselves.</remarks>
internal static unsafe class AmpereLowerOracle
{
    internal const string Env = "BFS_ORACLE";

    internal static bool Available => Environment.GetEnvironmentVariable(Env) is { } path && File.Exists(path);

    [DllImport("bfs_oracle", CallingConvention = CallingConvention.Cdecl)]
    private static extern int bfs_lower(byte* code, uint size, int geometry, int tensor, int tessellation, ulong address,
        ulong* output, uint capacity, uint* count, uint* errorOffset, byte* error, uint errorCapacity);

    static AmpereLowerOracle()
    {
        NativeLibrary.SetDllImportResolver(typeof(AmpereLowerOracle).Assembly, (name, _, _) =>
            name == "bfs_oracle" && Environment.GetEnvironmentVariable(Env) is { } path ? NativeLibrary.Load(path) : IntPtr.Zero);
    }

    internal readonly record struct Result(bool Ok, ulong[] Instructions, string Error, uint ErrorOffset);

    internal static Result Lower(ReadOnlySpan<byte> code, bool geometry, int tessellation, ulong address)
    {
        ulong[] output = new ulong[code.Length * 4 + 1024];
        byte[] error = new byte[256];
        uint count, errorOffset;
        int status;
        fixed (byte* c = code)
        fixed (ulong* o = output)
        fixed (byte* e = error)
            status = bfs_lower(c, (uint)code.Length, geometry ? 1 : 0, 1, tessellation, address, o, (uint)output.Length, &count, &errorOffset, e, (uint)error.Length);
        string message = System.Text.Encoding.ASCII.GetString(error).TrimEnd('\0');
        return status == 0 ? new Result(true, output[..(int)count], "", 0) : new Result(false, [], status == 2 ? "output buffer too small" : message, errorOffset);
    }
}
