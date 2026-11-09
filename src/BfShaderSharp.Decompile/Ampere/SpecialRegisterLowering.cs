namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers reads of special registers (S2R and S2UR) and the fragment discard.</summary>
/// <remarks>
/// The special registers that Maxwell's S2R handles keep Maxwell's numbering: lane id, invocation id and info, the Y
/// direction, thread kill, the W scale factors, thread and workgroup ids and the lane masks. The variable-rate
/// shading register reads as a 1x1 fragment, since variable-rate shading is not modelled. Any other register is
/// rejected. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class SpecialRegisterLowering
{
    private const uint VariableRate = 132;

    public static void Register(OpcodeTable table)
    {
        table.Add(Read, 0x919, 0x9c3);
        table.Add(Discard, 0x95b);
        table.Add(ClockZero, 0x805);
    }

    private static bool Supported(uint special) =>
        special == 0 || (special >= 17 && special <= 19) || (special >= 29 && special <= 35) || special is 37 or 38 or 39 or 56 or 57 or 58 or 59 or 60;

    private static bool Read(InstructionContext c)
    {
        uint special = c.Bits(72, 8);
        uint target = c.Opcode == 0x9c3 ? c.UniformRegister((uint)c.Dest) : (uint)c.Dest;
        if (special == VariableRate && c.Insn.W1 == 0 && c.Insn.W2 == (special << 8))
        {
            // Without variable-rate shading every fragment is 1x1, its width and height in bytes 1 and 2.
            if (target == uint.MaxValue) return c.Fail("native uniform special destination");
            c.MovImm(target, 0x00010100);
            return true;
        }

        if (c.Insn.W1 != 0 || c.Insn.W2 != (special << 8) || !Supported(special)) return c.Fail("native special register");
        if (target == uint.MaxValue) return c.Fail("native uniform special destination");
        c.Emit(0xf0c8000000000000UL | c.Predicate | target | ((ulong)special << 20));
        return true;
    }

    // Fragment discard; the execution predicate is preserved.
    private static bool Discard(InstructionContext c)
    {
        if (c.Insn.W1 != 0 || c.Insn.W2 != 0x03800000) return c.Fail("native fragment discard mode");
        c.Emit(0xe33000000000000fUL | c.Predicate);
        return true;
    }

    // CS2R SRZ: the zero special register, read into a register pair.
    private static bool ClockZero(InstructionContext c)
    {
        if (c.Bits(72, 8) != 255 || c.Bits(80, 1) == 0 || c.Dest >= 249) return c.Fail("native special-register read");
        c.MovImm((uint)c.Dest, 0);
        c.MovImm((uint)c.Dest + 1, 0);
        return true;
    }
}
