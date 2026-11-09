namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers global-memory loads and stores through a 64-bit address held in registers or built from a uniform pointer.</summary>
/// <remarks>
/// Sizes follow Maxwell: 8, 16, 32, 64 and 128 bits, signed or unsigned for the narrow loads. A uniform base is added
/// to the register address into a scratch pair; with a memory descriptor the register pair already holds the whole
/// address and the uniform pair is only a cache policy. A store of zero wider than one register is written from
/// zeroed scratch registers aligned to the access. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class GlobalMemoryLowering
{
    private const uint Reserved = 246;

    public static void Register(OpcodeTable table)
    {
        table.Add(Load, 0x381, 0x981);
        table.Add(Store, 0x386, 0x986);
    }

    // Adds the uniform pointer to the register address into R250:R251, with the high word taking the carry.
    private static void AddUniformBase(InstructionContext c, uint uniformAddress, uint address)
    {
        c.Emit(0x5c10000000000000UL | c.Predicate | 250 | ((ulong)uniformAddress << 8) | ((ulong)address << 20) | (1UL << 47));
        c.Emit(0x5c10000000000000UL | c.Predicate | 251 | ((ulong)(uniformAddress + 1) << 8) | ((ulong)(c.Bits(90, 1) != 0 ? address + 1 : 255) << 20) | (1UL << 43));
    }

    // LDG.E of 8 to 128 bits, register or uniform 64-bit address. Sizes as Maxwell's: U8, S8, U16, S16, 32, 64, 128.
    private static bool Load(InstructionContext c)
    {
        uint size = c.Bits(73, 3);
        bool scopedDescriptor = c.Opcode == 0x981 && c.Insn.W2 == 0x0c1ebd00;
        uint address = c.Bits(24, 8);
        if (size > 6 || (c.Opcode == 0x381 && c.Bits(32, 8) != 0) || (size == 5 && (c.Dest & 1) != 0) || (size == 6 && (c.Dest & 3) != 0)) return c.Fail("native global load mode");
        if (c.Opcode == 0x381)
        {
            // Bit 77 (strong/scoped ordering) needs nothing extra from a Maxwell LDG.
            if ((c.Insn.W2 & ~0x2e00U) != 0x001e8100 || (address & 1) != 0 || address >= Reserved) return c.Fail("native global load mode");
        }
        else if (!ResolveUniformAddress(c, scopedDescriptor, ref address))
        {
            return false;
        }

        c.Emit(c.Base(0xeed0000000000000UL) | ((ulong)address << 8) | ((ulong)c.Bits(40, 24) << 20) | (1UL << 45) | (InstructionContext.Flag(scopedDescriptor) << 46) | ((ulong)size << 48));
        return true;
    }

    private static bool ResolveUniformAddress(InstructionContext c, bool scopedDescriptor, ref uint address)
    {
        // The uniform base sits where a register source would (bits 32-37).
        uint uniformAddress = c.Bits(38, 2) != 0 ? uint.MaxValue : c.UniformRegister(c.Bits(32, 6));
        // desc[URn][Ra.64]: the uniform pair is a memory (cache policy) descriptor and Ra the whole address.
        bool descriptorAddress = scopedDescriptor || (c.Insn.W2 & ~(0x3fU | 0xe00U)) == 0x0c1e1100;
        if ((!descriptorAddress && (c.Insn.W2 & ~(0x04000000U | 0x3fU | 0xe00U | 0x2000U)) != 0x081e8100) || uniformAddress == uint.MaxValue || (uniformAddress & 1) != 0)
            return c.Fail("native global load uniform addressing mode");
        if (descriptorAddress)
            return ((address & 1) == 0 && address < Reserved) || c.Fail("native global load register address pair");
        if (address == 255)
        {
            address = uniformAddress;
            return true;
        }

        if (c.Bits(90, 1) != 0 && ((address & 1) != 0 || address >= Reserved)) return c.Fail("native global load register address pair");
        AddUniformBase(c, uniformAddress, address);
        address = 250;
        return true;
    }

    // Global store of 32/64/128 bits, 64-bit address.
    private static bool Store(InstructionContext c)
    {
        uint data = c.Bits(32, 8), size = c.Bits(73, 3);
        if (size > 6 || (data != 255 && ((size == 5 && (data & 1) != 0) || (size == 6 && (data & 3) != 0) || data + (size < 4 ? 1U : 1U << (int)(size - 4)) > Reserved)))
            return c.Fail("native global store size");
        if (data == 255 && size > 4)
        {
            // Zeros wider than one register: store from zeroed scratch registers (aligned to the access: R248-R251 for 128 bits, R250-R251 for 64).
            data = size == 6 ? 248U : 250U;
            for (uint i = 0; i < 1U << (int)(size - 4); i++) c.MovImm(data + i, 0);
        }

        uint address = c.Bits(24, 8);
        bool extended = c.Bits(72, 1) != 0;
        if (c.Opcode == 0x986)
        {
            if (!ResolveUniformStoreAddress(c, extended, ref address)) return false;
        }
        else if ((c.Insn.W2 & ~0xe00U) != 0x0010a100)
        {
            return c.Fail("native global store register addressing mode");
        }

        c.Emit(0xeed8000000000000UL | c.Predicate | data | ((ulong)address << 8) | ((ulong)c.Bits(40, 24) << 20) | (InstructionContext.Flag(extended) << 45) | ((ulong)size << 48));
        return true;
    }

    private static bool ResolveUniformStoreAddress(InstructionContext c, bool extended, ref uint address)
    {
        uint uniformAddress = c.UniformRegister(c.Bits(64, 6));
        bool descriptorAddress = (c.Insn.W2 & ~(0x3fU | 0xe00U)) == 0x0c101100;
        if ((!descriptorAddress && (c.Insn.W2 & ~(0x04000000U | 0x3fU | 0xe00U)) != 0x0810a100) || uniformAddress == uint.MaxValue || (uniformAddress & 1) != 0 || !extended)
            return c.Fail("native global store uniform addressing mode");
        if (descriptorAddress)
            return ((address & 1) == 0 && address < Reserved) || c.Fail("native global store register address pair");
        if (address == 255)
        {
            address = uniformAddress;
            return true;
        }

        if (c.Bits(90, 1) != 0 && ((address & 1) != 0 || address >= 254)) return c.Fail("native global store register address pair");
        AddUniformBase(c, uniformAddress, address);
        address = 250;
        return true;
    }
}
