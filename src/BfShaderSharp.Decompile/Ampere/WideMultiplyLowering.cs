namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers IMAD.HI and IMAD.WIDE: 32 by 32 to 64-bit multiplies with an optional 64-bit addend and carry output.</summary>
/// <remarks>
/// The product is built by base-2^16 long multiplication in the scratch registers: the high word in R253 and the low
/// word in R250. A signed product corrects the high word by subtracting the other operand where one is negative. The
/// addend pair is added with carry; a carry-out predicate is derived from the carries of both words. The second
/// operand is a register, an immediate (0x8xx) or a constant buffer (0xa27); 0x625 takes it from bits 64-71 and adds a
/// constant-buffer pair. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class WideMultiplyLowering
{
    private readonly record struct Shape(bool Wide, bool Immediate, bool Signed, uint Addend, uint Carry, uint A, uint B);

    public static void Register(OpcodeTable table) => table.Add(Lower, 0x225, 0x227, 0x625, 0x825, 0x827, 0xa27);

    private static bool Lower(InstructionContext c)
    {
        uint opcode = c.Opcode;
        bool wide = opcode == 0x225 || opcode == 0x625 || opcode == 0x825;
        bool immediate = opcode == 0x825 || opcode == 0x827, signedProduct = c.Bits(73, 1) != 0;
        uint addend = opcode == 0x625 ? 255U : c.Bits(64, 8), carry = c.Bits(81, 3);
        if (!Supported(c, wide, signedProduct, addend, carry)) return c.Fail("native high multiply mode");
        uint a = c.Bits(24, 8);
        uint b = immediate ? c.Insn.W1 : opcode == 0xa27 ? 254U : c.Bits(opcode == 0x625 ? 64 : 32, 8);
        Shape shape = new(wide, immediate, signedProduct, addend, carry, a, b);
        if (opcode == 0xa27) c.Emit(0x4c98000000000000UL | c.Predicate | 254 | (15UL << 39) | c.Cbuf());
        if (signedProduct) SignedCorrection(c, shape);
        // Base-2^16 long multiplication. Each intermediate sum fits U32.
        IntegerMath m = c.Integer;
        m.Xmad(250, a, b, 255, false, false, 0, immediate);
        m.Xmad(251, a, b, 250, true, false, 2, immediate);
        m.Xmad(250, a, b, 251, false, true, 1, immediate);
        m.Xmad(252, a, b, 251, true, true, 2, immediate);
        c.Emit(0x3828000000000000UL | c.Predicate | 250 | (250UL << 8) | (16UL << 20));
        if (signedProduct) c.Emit(0x5c10000000000000UL | c.Predicate | 250 | (250UL << 8) | (253UL << 20) | (1UL << 48));
        if (!wide && addend == 255 && carry == 7)
        {
            c.Emit(c.Base(0x5c10000000000000UL) | (250UL << 8) | (252UL << 20));
            return true;
        }

        AddAddend(c, shape);
        return true;
    }

    private static bool Supported(InstructionContext c, bool wide, bool signedProduct, uint addend, uint carry)
    {
        uint opcode = c.Opcode;
        return !((c.Insn.W2 & ~0x000e02ffU) != 0x07800000 ||
                 ((opcode == 0x225 || opcode == 0x227) && (c.Insn.W1 & ~0xffU) != 0) ||
                 ((opcode == 0xa27 || opcode == 0x625) && (!c.ValidCbuf() || c.Bits(62, 2) != 0)) ||
                 (opcode == 0x625 && c.Bits(38, 16) > 0xfff8) || (addend != 255 && (addend & 1) != 0) ||
                 (wide && (c.Dest == 255 || (c.Dest & 1) != 0)) || (signedProduct && carry != 7));
    }

    // The signed high word is the unsigned one minus (a < 0 ? b : 0) + (b < 0 ? a : 0), which R253 holds (R252 is free until the multiplication).
    private static void SignedCorrection(InstructionContext c, Shape s)
    {
        uint bRegister = s.Immediate ? 254U : s.B;
        if (s.Immediate) c.MovImm(254, s.B);
        c.Emit(0x3828000000000000UL | c.Predicate | 253 | ((ulong)s.A << 8) | (31UL << 20) | (1UL << 48));
        c.Emit(0x5c40000000000000UL | c.Predicate | 253 | (253UL << 8) | ((ulong)bRegister << 20) | (7UL << 48));
        c.Emit(0x3828000000000000UL | c.Predicate | 252 | ((ulong)bRegister << 8) | (31UL << 20) | (1UL << 48));
        c.Emit(0x5c40000000000000UL | c.Predicate | 252 | (252UL << 8) | ((ulong)s.A << 20) | (7UL << 48));
        c.Emit(0x5c10000000000000UL | c.Predicate | 253 | (253UL << 8) | (252UL << 20));
    }

    // High word in R253 and low word in R250 (as UIMAD.WIDE.U32), then the addend pair is added.
    private static void AddAddend(InstructionContext c, Shape s)
    {
        c.Emit(0x5c10000000000000UL | c.Predicate | 253 | (250UL << 8) | (252UL << 20));
        IntegerMath m = c.Integer;
        m.Xmad(250, s.A, s.B, 255, false, false, 0, s.Immediate);
        m.Xmad(251, s.A, s.B, 255, true, false, 0, s.Immediate);
        m.Xmad(251, s.A, s.B, 251, false, true, 0, s.Immediate);
        c.Emit(0x5c18000000000000UL | c.Predicate | 250 | (251UL << 8) | (250UL << 20) | (16UL << 39));
        uint low = s.Addend, high = s.Addend == 255 ? 255U : s.Addend + 1;
        if (c.Opcode == 0x625)
        {
            c.Emit(0x4c98000000000000UL | c.Predicate | 252 | (15UL << 39) | c.Cbuf());
            c.Emit(0x4c98000000000000UL | c.Predicate | 254 | (15UL << 39) | (c.Cbuf() + (1UL << 20)));
            low = 252;
            high = 254;
        }

        uint highDestination = s.Wide ? (uint)c.Dest + 1 : (uint)c.Dest;
        c.Emit(0x5c10000000000000UL | c.Predicate | 250 | (250UL << 8) | ((ulong)low << 20) | (1UL << 47));
        if (s.Carry == 7)
        {
            c.Emit(0x5c10000000000000UL | c.Predicate | highDestination | (253UL << 8) | ((ulong)high << 20) | (1UL << 43));
            if (s.Wide) c.Emit(c.Base(0x5c10000000000000UL) | (250UL << 8) | (255UL << 20));
            return;
        }

        CarryOutput(c, s, high, highDestination);
    }

    // Maxwell IADD has no .X with .CC: R251 takes the low carry, R254 counts the high word's.
    private static void CarryOutput(InstructionContext c, Shape s, uint high, uint highDestination)
    {
        c.Emit(0x5c10000000000000UL | c.Predicate | 251 | (255UL << 8) | (255UL << 20) | (1UL << 43));
        c.Emit(0x5c10000000000000UL | c.Predicate | 253 | (253UL << 8) | ((ulong)high << 20) | (1UL << 47));
        c.Emit(0x5c10000000000000UL | c.Predicate | 254 | (255UL << 8) | (255UL << 20) | (1UL << 43));
        c.Emit(0x5c10000000000000UL | c.Predicate | 253 | (253UL << 8) | (251UL << 20) | (1UL << 47));
        c.Emit(0x5c10000000000000UL | c.Predicate | 254 | (254UL << 8) | (255UL << 20) | (1UL << 43));
        c.Emit(0x5c98000000000000UL | c.Predicate | highDestination | (253UL << 20) | (15UL << 39));
        if (s.Wide) c.Emit(c.Base(0x5c98000000000000UL) | (250UL << 20) | (15UL << 39));
        c.Emit(0x5b60000000000000UL | c.Predicate | 7 | ((ulong)s.Carry << 3) | (254UL << 8) | (255UL << 20) | (7UL << 39) | (5UL << 49));
    }
}
