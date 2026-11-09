namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the select (SEL, FSEL), three-input logic (LOP3), integer absolute value (IABS) and integer min/max (IMNMX).</summary>
/// <remarks>
/// FSEL applies the floating operand modifiers before selecting. LOP3 keeps any lookup table and the optional
/// predicate result. IABS is the maximum of the value and its negation, using a scratch register. An IMNMX immediate
/// that fits a signed 20-bit value is encoded directly; a larger one goes through scratch. Ported from the Eden
/// emulator (GPL-3.0-or-later).
/// </remarks>
internal static class SelectLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(Select, 0x207, 0x807, 0xa07, 0xc07, 0x208, 0x808, 0xa08);
        table.Add(Logic, 0x212, 0x812, 0xa12, 0xc12);
        table.Add(Absolute, 0x213, 0xa13, 0xc13);
        table.Add(MinMax, 0x217, 0x817, 0xa17, 0xc17);
    }

    private static bool Select(InstructionContext c)
    {
        bool floating = (c.Opcode & 0x1ff) == 0x008;
        uint a = c.Operands.Operand(0, floating), b = c.Operands.Operand(1, floating);
        c.Emit(c.Base(0x5ca0000000000000UL) | ((ulong)a << 8) | ((ulong)b << 20) | ((ulong)c.Bits(87, 3) << 39) | ((ulong)c.Bits(90, 1) << 42));
        return true;
    }

    // LOP3.LUT, any table, optional P = (result != 0).
    private static bool Logic(InstructionContext c)
    {
        uint opcode = c.Opcode, lut = c.Bits(72, 8), target = c.Bits(81, 3), b;
        if (c.Bits(80, 1) != 0 || c.Bits(87, 4) != 15 || (opcode == 0x212 && c.Bits(40, 24) != 0) || (opcode == 0xa12 && c.Bits(62, 2) != 0))
            return c.Fail("native LUT/predicate mode");
        if (opcode == 0x212)
        {
            b = c.Bits(32, 8);
        }
        else if (opcode == 0x812)
        {
            c.MovImm(251, c.Insn.W1);
            b = 251;
        }
        else if (opcode == 0xc12)
        {
            b = c.UniformRegister(c.Bits(32, 8));
            if (b == uint.MaxValue) return c.Fail("native uniform logic source");
        }
        else
        {
            b = c.Operands.Operand(1, false);
        }

        c.Emit(0x5be0000000000000UL | c.Predicate | c.Dest | ((ulong)c.Bits(24, 8) << 8) | ((ulong)b << 20) | ((ulong)lut << 28) |
               ((target == 7 ? 0UL : 3UL) << 36) | ((ulong)c.Bits(64, 8) << 39) | ((ulong)target << 48));
        return true;
    }

    // IABS: max(x, 0 - x), signed.
    private static bool Absolute(InstructionContext c)
    {
        if ((c.Insn.W2 & ~(c.Opcode == 0xc13 ? 0x08000000U : 0U)) != 0) return c.Fail("native integer absolute mode");
        uint source = c.Operands.Operand(1, false);
        c.Emit(0x5c10000000000000UL | c.Predicate | 250 | (255UL << 8) | ((ulong)source << 20) | (1UL << 48));
        c.Emit(c.Base(0x5c20000000000000UL) | ((ulong)source << 8) | (250UL << 20) | (15UL << 39) | (1UL << 48));
        return true;
    }

    // IMNMX with a register, immediate, constant-buffer or uniform operand.
    private static bool MinMax(InstructionContext c)
    {
        uint opcode = c.Opcode, word = c.Insn.W1;
        if ((c.Insn.W2 & ~(0x04000200U | (opcode == 0xc17 ? 0x08000000U : 0U))) != 0x03800000 || (opcode != 0xa17 && opcode != 0x817 && word > 255U) ||
            (opcode == 0xa17 && (!c.ValidCbuf() || c.Bits(62, 2) != 0)))
            return c.Fail("native integer min/max mode");
        // An immediate is a signed 20-bit value: 19 bits at 20, the sign in bit 56.
        bool shortImmediate = opcode == 0x817 && (word <= 0x7ffffU || word >= 0xfff80000U);
        if (opcode == 0x817 && !shortImmediate) c.MovImm(251, word);
        uint source = opcode == 0xc17 ? c.UniformRegister(word) : opcode == 0xa17 ? c.Operands.Operand(1, false)
                    : opcode == 0x817 ? (shortImmediate ? word & 0x7ffffU : 251U) : word;
        if (source == uint.MaxValue) return c.Fail("native uniform min/max operand");
        c.Emit(c.Base(shortImmediate ? 0x3820000000000000UL : 0x5c20000000000000UL) | ((ulong)c.Bits(24, 8) << 8) | ((ulong)source << 20) |
               ((ulong)(shortImmediate ? word >> 31 : 0U) << 56) | ((ulong)c.Bits(87, 4) << 39) | ((ulong)c.Bits(73, 1) << 48));
        return true;
    }
}
