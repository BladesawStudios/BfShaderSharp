namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the program-counter load (geometry stage) and the stage-buffer read and write (ISBERD, ISBEWR).</summary>
/// <remarks>
/// The program-counter load appears only in a geometry stage's exit setup and becomes the known address of the
/// instruction. In the geometry and tessellation stages a stage-buffer read is a register move. Otherwise (task and
/// mesh programs) the access is kept as a pseudo instruction in Maxwell's free 0xefc0 encoding, which the frontend
/// reads: data or destination in 0-7, address register in 8-15, offset in 20-43, then the 32-bit, attribute, skew,
/// output and write flags. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class StageBufferLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(ProgramCounter, 0x34e);
        table.Add(Access, 0x923, 0x927);
    }

    private static bool ProgramCounter(InstructionContext c)
    {
        if (!c.State.Options.Geometry) return c.Fail("unsupported native opcode");
        uint limit = c.State.UsesUniformBank ? c.State.HighUniformBase : 246U;
        if (c.State.Options.NativeCodeAddress == 0 || c.Bits(24, 8) != 0 || c.Insn.W1 != 0 || c.Insn.W2 != 0 || (c.Dest != 255 && c.Dest + 1 >= limit) || c.NativePredicate != 7)
            return c.Fail("native program counter load mode");
        if (c.Dest == 255) return true;
        ulong address = c.State.Options.NativeCodeAddress + (ulong)c.Index * 16;
        c.MovImm((uint)c.Dest, (uint)address);
        c.MovImm((uint)c.Dest + 1, (uint)(address >> 32));
        return true;
    }

    private static bool Access(InstructionContext c)
    {
        TessellationStage tessellation = c.State.Options.Tessellation;
        if (c.State.Options.Geometry || tessellation != TessellationStage.None)
        {
            if (c.Opcode != 0x923 || c.Insn.W1 != 0 || c.Insn.W2 != 0 || c.Bits(24, 8) == 255)
                return c.Fail(tessellation != TessellationStage.None ? "native tessellation stage buffer read mode" : "native geometry stage buffer read mode");
            c.Emit(c.Base(0x5c98000000000000UL) | (15UL << 39) | ((ulong)c.Bits(24, 8) << 20));
            return true;
        }

        // Ra 24-31, offset 40-63, ISBERD dest 16-23, ISBEWR data 32-39; 75 = .32 (else U8), 76-77 = 3 .ATTR (0 the map region), 78 = .SKEW, 79 = .O (the output buffer).
        bool write = c.Opcode == 0x927, output = c.Bits(79, 1) != 0;
        uint region = c.Bits(76, 2);
        if ((c.Insn.W2 & ~0xf800U) != 0 || (region != 0 && region != 3) || (write && !output) || (!write && region != 3) || (!write && c.Dest == 255))
            return c.Fail("native stage buffer access mode");
        c.Emit(0xefc0000000000000UL | c.Predicate | (write ? c.Bits(32, 8) : c.Dest) | ((ulong)c.Bits(24, 8) << 8) | ((ulong)c.Bits(40, 24) << 20) |
               ((ulong)c.Bits(75, 1) << 44) | (InstructionContext.Flag(region == 3) << 45) | ((ulong)c.Bits(78, 1) << 46) | (InstructionContext.Flag(output) << 47) |
               (InstructionContext.Flag(write) << 48));
        return true;
    }
}
