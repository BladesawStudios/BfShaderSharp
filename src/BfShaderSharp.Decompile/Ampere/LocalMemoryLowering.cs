namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers local-memory scalar loads and stores used for register spills (0x387, 0x983, 0x987).</summary>
/// <remarks>
/// The size field selects 8, 16, 32, 64 or 128 bits (the narrow loads signed or unsigned); wider accesses become one
/// Maxwell word access per register. A uniform base is added to the register address in scratch, and a wide load whose
/// address overlaps its destination copies the address first. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class LocalMemoryLowering
{
    public static void Register(OpcodeTable table) => table.Add(Spill, 0x387, 0x983, 0x987);

    private static bool Spill(InstructionContext c)
    {
        bool store = c.Opcode != 0x983, uniformAddress = c.Bits(91, 1) != 0;
        uint size = c.Bits(73, 3), data = c.Bits(32, 8), offset = c.Bits(40, 24), address = c.Bits(24, 8);
        uint words = size == 6 ? 4U : size == 5 ? 2U : 1U, value = store ? data : (uint)c.Dest, bytes = size < 2 ? 1U : size < 4 ? 2U : 4U;
        uint registerLimit = c.State.UsesUniformBank ? c.State.HighUniformBase : 246U, valueLimit = c.State.UsesUniformBank ? c.State.HighUniformBase : 248U;
        uint allowed = (store ? 0xe00U : 0x200e00U) | (uniformAddress ? 0x08000000U | (store ? 0x3fU : 0U) : 0U);
        if ((address != 255 && address >= registerLimit) || (store && c.Bits(16, 8) != 0) || (!store && data != 0 && !uniformAddress) ||
            (value != 255 && (value + words > valueLimit || (value & (words - 1)) != 0)) || (!store && value == 255) || (c.Opcode == 0x987 && !uniformAddress) ||
            (offset & (bytes - 1)) != 0 || offset + bytes * words > 0x800000 || size > 6 || (c.Insn.W2 & ~allowed) != 0x00100000U)
            return c.Fail("native local scalar spill mode");
        if (uniformAddress)
        {
            uint uniform = c.UniformRegister(store ? c.Bits(64, 6) : data);
            if (uniform == uint.MaxValue) return c.Fail("native local spill uniform address");
            if (address == 255)
            {
                address = uniform;
            }
            else if (uniform != 255)
            {
                c.Emit(0x5c10000000000000UL | c.Predicate | 252 | ((ulong)uniform << 8) | ((ulong)address << 20));
                address = 252;
            }
        }

        if (!store && words > 1 && address >= value && address < value + words)
        {
            TextureSupport.Copy(c, 252, address);
            address = 252;
        }

        for (uint word = 0; word < words; word++)
            c.Emit((store ? 0xef50000000000000UL : 0xef40000000000000UL) | c.Predicate | (value == 255 ? 255U : value + word) | ((ulong)address << 8) |
                   ((ulong)(offset + 4 * word) << 20) | ((ulong)(size < 4 ? size : 4U) << 48));
        return true;
    }
}
