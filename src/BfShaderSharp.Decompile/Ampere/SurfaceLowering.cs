using System.Numerics;

namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the surface (image) loads, stores, atomics and reductions.</summary>
/// <remarks>
/// Formatted surface accesses take a descriptor from a bound constant buffer slot (at most one such buffer per program)
/// or, for the bindless forms, from a register. Dimension codes are Maxwell's. Register operands must stay below the
/// range the lowering reserves for its own scratch registers. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class SurfaceLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(BindlessAccess, 0x998, 0x99c);
        table.Add(Load, 0xb97);
        table.Add(StoreBytes, 0xb9d);
        table.Add(Store, 0xb9b);
        table.Add(Atomic, 0x593);
        table.Add(Reduction, 0x59f);
    }

    private static uint Reserved(InstructionContext c) => c.State.UsesUniformBank ? c.State.HighUniformBase : 246U;

    private static uint MaxCoordinate(uint dimension) => dimension >= 4 ? 252U : dimension == 3 ? 253U : 254U;

    private static uint AlignmentOf(uint count) => count == 3 ? 4U : count;

    private static bool BindlessAccess(InstructionContext c)
    {
        bool store = c.Opcode == 0x99c;
        uint data = c.Bits(store ? 32 : 16, 8), coordinate = c.Bits(24, 8), handle = c.Bits(64, 8);
        uint channels = c.Bits(72, 4), dimension = c.Bits(61, 3), count = (uint)BitOperations.PopCount(channels), reserved = Reserved(c);
        if ((c.Insn.W1 & ~(store ? 0xe00000ffU : 0xe0000000U)) != 0 || (c.Insn.W2 & ~0xfffU) != (store ? 0x00100000U : 0x001e0000U) ||
            (c.Insn.W3 & 0xc00001ffU) != 0 || dimension < 3 || dimension > 5 || channels == 0 || handle >= reserved || data + count > reserved ||
            coordinate + (dimension >= 4 ? 3U : 2U) > reserved || (store && (c.Dest != 0 || (channels != 1 && channels != 3 && channels != 15))) ||
            (!store && data % AlignmentOf(count) != 0))
            return c.Fail("native bindless formatted surface mode");
        c.Emit((store ? 0xeb20000000000000UL : 0xeb00000000000000UL) | c.Predicate | data | ((ulong)coordinate << 8) | ((ulong)channels << 20) |
               ((ulong)dimension << 33) | ((ulong)handle << 39));
        return true;
    }

    // SULD.P.1D/buffer/2D/2D_ARRAY/3D.IGN, direct descriptor, channel mask at 72-75.
    private static bool Load(InstructionContext c)
    {
        uint binding = c.Bits(54, 5), offset = c.Bits(40, 14), coordinate = c.Bits(24, 8), channels = c.Bits(72, 4);
        uint dimension = c.Bits(61, 3); // Maxwell's dimension codes.
        uint reserved = Reserved(c), count = (uint)BitOperations.PopCount(channels), dest = (uint)c.Dest;
        if ((c.Insn.W2 & ~0xf00U) != 0x001e0000 || channels == 0 || dimension == 2 || dimension > 5 || binding >= 18 || offset > 0x1fff ||
            c.Bits(59, 2) != 0 || c.Dest == 255 || coordinate > MaxCoordinate(dimension) ||
            (dimension < 2 && (coordinate >= reserved || dest + count > reserved || dest % AlignmentOf(count) != 0)))
            return c.Fail("native formatted surface load mode");
        if (!c.BindTextureBuffer(binding)) return c.Fail("native surface descriptor buffer conflict");
        c.Emit(c.Base(0xeb00000000000000UL) | ((ulong)coordinate << 8) | ((ulong)channels << 20) | ((ulong)dimension << 33) | ((ulong)offset << 36) | (1UL << 51));
        return true;
    }

    private static bool StoreBytes(InstructionContext c)
    {
        uint binding = c.Bits(54, 5), offset = c.Bits(40, 14), data = c.Bits(32, 8), coordinate = c.Bits(24, 8), reserved = Reserved(c);
        if (c.Insn.W2 != 0x0010a900 || c.Bits(61, 3) != 3 || c.Bits(16, 8) != 0 || binding >= 18 || offset > 0x1fff || c.Bits(59, 2) != 0 ||
            data >= reserved || coordinate + 1 >= reserved)
            return c.Fail("native byte-addressed surface store mode");
        if (!c.BindTextureBuffer(binding)) return c.Fail("native byte-addressed surface descriptor buffer conflict");
        c.Emit(0xeb38000000000000UL | c.Predicate | data | ((ulong)coordinate << 8) | (4UL << 20) | (1UL << 23) | (3UL << 33) | ((ulong)offset << 36));
        return true;
    }

    // SUST.P.1D/buffer/2D/3D.IGN, direct descriptor.
    private static bool Store(InstructionContext c)
    {
        uint binding = c.Bits(54, 5), offset = c.Bits(40, 14), data = c.Bits(32, 8), coordinate = c.Bits(24, 8);
        uint dimension = c.Bits(61, 3), mask = c.Bits(72, 4), mode = c.Insn.W2 & ~0xf00U, reserved = Reserved(c);
        bool strong = mode == 0x0010a000; // Strong stores are fenced on both sides.
        if (mask == 0 || (mode != 0x00100000 && !strong) || dimension == 2 || dimension > 5 || binding >= 18 || offset > 0x1fff || c.Bits(59, 2) != 0 ||
            data > 252 || coordinate > MaxCoordinate(dimension) ||
            (dimension < 2 && (coordinate >= reserved || data + (uint)BitOperations.PopCount(mask) > reserved || (mask != 1 && mask != 3 && mask != 15))))
            return c.Fail("native formatted surface store mode");
        // A second descriptor buffer: SUST.B with the handle, loaded from that buffer, in R250.
        bool bindless = !c.BindTextureBuffer(binding);
        if (bindless) c.Emit(0x4c98000000070000UL | 250 | (15UL << 39) | ((ulong)offset << 20) | ((ulong)binding << 34));
        if (strong) c.Emit(0xef98000000000000UL | c.Predicate | (1UL << 8)); // MEMBAR.GL
        c.Emit(0xeb20000000000000UL | c.Predicate | data | ((ulong)coordinate << 8) | ((ulong)mask << 20) | ((ulong)dimension << 33) |
               (bindless ? 250UL << 39 : ((ulong)offset << 36) | (1UL << 51)));
        if (strong) c.Emit(0xef98000000000000UL | c.Predicate | (1UL << 8));
        return true;
    }

    private static bool Atomic(InstructionContext c)
    {
        uint binding = c.Bits(54, 5), offset = c.Bits(40, 14), data = c.Bits(32, 8), coordinate = c.Bits(24, 8);
        uint dimension = c.Bits(61, 3), operation = c.Bits(87, 4), reserved = Reserved(c);
        uint coordinates = dimension >= 4 ? 3U : dimension == 3 ? 2U : 1U;
        if ((c.Insn.W2 & ~0x07800000U) != 0x001ee000 || operation > 8 || dimension == 2 || dimension > 5 || binding >= 18 || offset > 0x1fff ||
            c.Bits(59, 2) != 0 || coordinate + coordinates > reserved || (data != 255 && data >= reserved) || (c.Dest != 255 && c.Dest >= reserved) ||
            (c.Insn.W3 & 0xc00001ffU) != 0)
            return c.Fail("native formatted surface atomic mode");
        if (!c.BindTextureBuffer(binding)) return c.Fail("native surface descriptor buffer conflict");
        c.Emit(c.Base(0xea00000000000000UL) | ((ulong)coordinate << 8) | ((ulong)data << 20) | ((ulong)operation << 29) | ((ulong)dimension << 33) | ((ulong)offset << 36));
        return true;
    }

    // SURED.D.ADD.STRONG.GPU.IGN U32, direct descriptor, buffer or 2D.
    private static bool Reduction(InstructionContext c)
    {
        uint binding = c.Bits(54, 5), offset = c.Bits(40, 14), data = c.Bits(32, 8), coordinate = c.Bits(24, 8), dimension = c.Bits(61, 3);
        if (c.Insn.W2 != 0x0010e000 || (dimension != 1 && dimension != 3) || binding >= 18 || offset > 0x1fff || c.Bits(59, 2) != 0 || coordinate > 253)
            return c.Fail("native surface reduction mode");
        if (!c.BindTextureBuffer(binding)) return c.Fail("native surface descriptor buffer conflict");
        // Maxwell SURED as the frontend decodes it: operand 0-7, coordinates 8-15, ADD and U32 are zero at 24 and 20, type 33 (1 = buffer, 3 = 2D), bound offset 36, bound 51.
        c.Emit(0xeb40000000000000UL | c.Predicate | data | ((ulong)coordinate << 8) | ((ulong)dimension << 33) | ((ulong)offset << 36) | (1UL << 51));
        return true;
    }
}
