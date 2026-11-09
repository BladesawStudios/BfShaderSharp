namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers atomic and reduction operations on shared and global memory.</summary>
/// <remarks>
/// Shared-memory atomics map onto ATOMS with a word-aligned offset; the population-count increment adds one per active
/// lane. Global atomics and reductions take a 64-bit address from a register pair or a uniform pointer and a signed
/// 20-bit offset, and operate on 32-bit words. The operation numbering matches Maxwell's. Ported from the Eden emulator
/// (GPL-3.0-or-later).
/// </remarks>
internal static class AtomicLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(GlobalReduction, 0x98e);
        table.Add(SharedIncrement, 0xf8c);
        table.Add(SharedAtomic, 0x38c);
        table.Add(GlobalAtomic, 0x3a8);
        table.Add(UniformGlobalAtomic, 0x9a8);
    }

    private static int SignedOffset(InstructionContext c) => (int)(c.Bits(40, 24) << 8) >> 8;

    // RED.E.{ADD..XOR}.STRONG.GPU U32 (op in bits 87-89), register address.
    private static bool GlobalReduction(InstructionContext c)
    {
        uint type = c.Bits(73, 3), operation = c.Bits(87, 3);
        bool uniformAddress = c.Bits(24, 8) == 255;
        uint variable = 0x03800e00U | (uniformAddress ? 0x3fU : 0U);
        uint mode = uniformAddress ? 0x0c10e100U : 0x0010e100U;
        int offset = SignedOffset(c);
        if ((c.Insn.W2 & ~variable) != mode || (type != 0 && type != 1 && type != 3) || (type == 3 && operation != 0)) return c.Fail("native global reduction mode");
        uint address = uniformAddress ? c.UniformRegister(c.Bits(64, 6)) : c.Bits(24, 8);
        if (address == uint.MaxValue || (address & 1) != 0 || offset < -0x80000 || offset > 0x7ffff) return c.Fail("native global reduction address");
        c.Emit(0xebf8000000000000UL | c.Predicate | c.Bits(32, 8) | ((ulong)address << 8) | ((ulong)type << 20) | ((ulong)operation << 23) |
               ((ulong)((uint)offset & 0xfffff) << 28) | (1UL << 48));
        return true;
    }

    // ATOMS.POPC.INC.32 with no returned value: one increment per active lane.
    private static bool SharedIncrement(InstructionContext c)
    {
        uint stride = c.Bits(78, 2), offset = c.Bits(40, 24);
        if (c.Dest != 255 || (c.Insn.W1 & 0xff) != 0 || (offset & 3) != 0 || (c.Insn.W2 & ~0xc000U) != 0x0d00003f) return c.Fail("native shared increment mode");
        uint address = c.Bits(24, 8);
        if (stride != 0 && address != 255)
        {
            c.Emit(0x3848000000000000UL | c.Predicate | 251 | ((ulong)address << 8) | ((ulong)(stride + 1) << 20));
            address = 251;
        }

        c.MovImm(250, 1);
        c.Emit(0xec00000000000000UL | c.Predicate | 255 | ((ulong)address << 8) | (250UL << 20) | ((ulong)(offset >> 2) << 30));
        return true;
    }

    // ATOMS (ADD/MIN/MAX/INC/DEC/AND/OR/XOR/EXCH, U32 or S32), aligned byte offset. The operation (bits 87-90) is numbered as in Maxwell ATOMS (52-55); bit 73 is S32.
    private static bool SharedAtomic(InstructionContext c)
    {
        uint offset = c.Bits(40, 24), operation = c.Bits(87, 4);
        if ((c.Insn.W2 & ~0x07800200U) != 0 || operation > 8 || (offset & 3) != 0) return c.Fail("native shared atomic mode");
        c.Emit(0xec00000000000000UL | c.Predicate | c.Dest | ((ulong)c.Bits(24, 8) << 8) | ((ulong)c.Bits(32, 8) << 20) | ((ulong)c.Bits(73, 1) << 28) |
               ((ulong)(offset >> 2) << 30) | ((ulong)operation << 52));
        return true;
    }

    // ATOMG.E.STRONG.GPU U32 at a 64-bit register address plus an offset.
    private static bool GlobalAtomic(InstructionContext c)
    {
        uint address = c.Bits(24, 8), operation = c.Bits(87, 4);
        int offset = SignedOffset(c);
        if ((c.Insn.W2 & ~0x07800000U) != 0x001ee100 || operation > 8 || (address & 1) != 0 || address >= 246 || offset < -0x80000 || offset > 0x7ffff)
            return c.Fail("native global atomic register mode");
        c.Emit(0xed00000000000000UL | c.Predicate | c.Dest | ((ulong)address << 8) | ((ulong)c.Bits(32, 8) << 20) | ((ulong)((uint)offset & 0xfffff) << 28) |
               (1UL << 48) | ((ulong)operation << 52));
        return true;
    }

    // ATOMG.E.ADD.STRONG.GPU U32 through a uniform pointer plus an offset.
    private static bool UniformGlobalAtomic(InstructionContext c)
    {
        uint address = c.UniformRegister(c.Bits(64, 6));
        int offset = SignedOffset(c);
        if (c.Bits(24, 8) != 255 || offset < -0x80000 || offset > 0x7ffff || (c.Insn.W2 & ~0x3fU) != 0x081ee140 || address == uint.MaxValue || (address & 1) != 0)
            return c.Fail("native global atomic mode");
        c.Emit(0xed00000000000000UL | c.Predicate | c.Dest | ((ulong)address << 8) | ((ulong)c.Bits(32, 8) << 20) | ((ulong)((uint)offset & 0xfffff) << 28) | (1UL << 48));
        return true;
    }
}
