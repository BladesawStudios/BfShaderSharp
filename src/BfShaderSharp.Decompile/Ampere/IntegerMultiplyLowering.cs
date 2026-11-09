namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the integer multiply-add family: IMAD in its low-word forms, IMAD.MOV, the dot product and the uniform wide multiply.</summary>
/// <remarks>
/// Maxwell has no 32-bit integer multiply, so a multiply is built from four XMAD half-word steps (see
/// <see cref="IntegerMath"/>). IMAD forms whose multiplier is zero, one or a power of two become moves, adds and
/// shifts; other forms must have the plain encoding or are rejected. The wide and high forms are in
/// <see cref="WideMultiplyLowering"/>. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class IntegerMultiplyLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(UniformAddend, 0xc24, 0xe24);
        table.Add(RegisterMultiply, 0x224);
        table.Add(UniformWideMultiply, 0x2a5);
        table.Add(DotProduct, 0x226);
        table.Add(Trap, 0x95c);
        table.Add(ConstantMultiply, 0xa24);
        table.Add(MoveOrMultiply, 0x424, 0x624);
        table.Add(CapturedMultiply, 0x824);
    }

    // IMAD low word with a uniform multiplier or addend. 0xe24 keeps the uniform addend in bits 32-37; bit 63 negates it ("RZ, RZ, -UR4").
    private static bool UniformAddend(InstructionContext c)
    {
        bool negateC = c.Opcode == 0xe24 && c.Bits(63, 1) != 0;
        if ((c.Insn.W2 & ~0x080002ffU) != 0x078e0000 || (c.Insn.W1 & ~(0xffU | (negateC ? 0x80000000U : 0U))) != 0) return c.Fail("native uniform-addend multiply mode");
        uint a = c.Operands.Operand(0, false), b = c.Operands.Operand(1, false), third = c.Operands.Operand(2, false);
        if (negateC)
        {
            c.Emit(0x5c10000000000000UL | c.Predicate | 252 | (255UL << 8) | ((ulong)third << 20) | (1UL << 48));
            third = 252;
        }

        c.Integer.MultiplyLow(a, b, third, false);
        return true;
    }

    // IMAD register / zero-product move.
    private static bool RegisterMultiply(InstructionContext c)
    {
        bool zeroProduct = c.Bits(24, 8) == 255 && c.Insn.W1 == 255;
        if (zeroProduct && c.Bits(75, 1) != 0 && (c.Insn.W2 & ~0xaffU) == 0x078e0000)
        {
            c.Emit(c.Base(0x5c10000000000000UL) | (255UL << 8) | ((ulong)c.Bits(64, 8) << 20) | (1UL << 48));
            return true;
        }

        if (zeroProduct && (c.Insn.W2 & ~0xffU) == 0x078e0000)
        {
            c.Emit(c.Base(0x5c98000000000000UL) | (15UL << 39) | ((ulong)c.Bits(64, 8) << 20));
            return true;
        }

        if (zeroProduct && (c.Insn.W2 & ~0x07800affU) == 0x000e0400 && (!c.RewrittenUniform || c.Bits(87, 3) == 7))
        {
            // IMAD.X RZ, RZ, c, Pn: the high word of a 64-bit add (bit 75 inverts c).
            uint addend = c.Bits(64, 8);
            c.MovImm(250, 1);
            c.Emit(0x5ca0000000000000UL | c.Predicate | 250 | (250UL << 8) | (255UL << 20) | ((ulong)c.Bits(87, 3) << 39) | ((ulong)c.Bits(90, 1) << 42));
            if (c.Bits(75, 1) != 0)
            {
                c.Emit(0x0400000000000000UL | c.Predicate | 251 | ((ulong)addend << 8) | (0xffffffffUL << 20) | (2UL << 53));
                addend = 251;
            }

            c.Emit(c.Base(0x5c10000000000000UL) | ((ulong)addend << 8) | (250UL << 20));
            return true;
        }

        if ((c.Insn.W2 & ~0x2ffU) != 0x078e0000 || (c.Insn.W1 & ~0xffU) != 0) return c.Fail("native integer register multiply mode");
        c.Integer.MultiplyLow(c.Bits(24, 8), c.Bits(32, 8), c.Bits(64, 8), false);
        return true;
    }

    // UIMAD.WIDE.U32 UR(d):UR(d+1) = URa * URb + URc:UR(c+1).
    private static bool UniformWideMultiply(InstructionContext c)
    {
        uint a = c.UniformRegister(c.Bits(24, 8)), b = c.UniformRegister(c.Bits(32, 8)), third = c.Bits(64, 8);
        uint thirdLow = c.UniformRegister(third), thirdHigh = third == 63 ? 255U : c.UniformRegister(third + 1);
        uint destinationLow = c.UniformRegister(c.Bits(16, 8)), destinationHigh = c.UniformRegister(c.Bits(16, 8) + 1);
        if (c.NativePredicate != 7 || (c.Insn.W1 & ~0xffU) != 0 || (c.Insn.W2 & ~0xffU) != 0x0f8e0000 || (c.Bits(16, 8) & 1) != 0 ||
            (third != 63 && (third & 1) != 0) || a == uint.MaxValue || b == uint.MaxValue || thirdLow == uint.MaxValue ||
            thirdHigh == uint.MaxValue || destinationLow == uint.MaxValue || destinationHigh == uint.MaxValue || destinationLow == 255)
            return c.Fail("native uniform wide multiply mode");
        // High word in R253 (base-2^16 long multiplication, as IMAD.HI), low word in R250.
        IntegerMath m = c.Integer;
        m.Xmad(250, a, b, 255, false, false, 0, false);
        m.Xmad(251, a, b, 250, true, false, 2, false);
        m.Xmad(250, a, b, 251, false, true, 1, false);
        m.Xmad(252, a, b, 251, true, true, 2, false);
        c.Emit(0x3828000000070000UL | 250 | (250UL << 8) | (16UL << 20));
        c.Emit(0x5c10000000070000UL | 253 | (250UL << 8) | (252UL << 20));
        m.Xmad(250, a, b, 255, false, false, 0, false);
        m.Xmad(251, a, b, 255, true, false, 0, false);
        m.Xmad(251, a, b, 251, false, true, 0, false);
        c.Emit(0x5c18000000070000UL | 250 | (251UL << 8) | (250UL << 20) | (16UL << 39));
        c.Emit(0x5c10000000070000UL | 250 | (250UL << 8) | ((ulong)thirdLow << 20) | (1UL << 47));
        c.Emit(0x5c10000000070000UL | destinationHigh | (253UL << 8) | ((ulong)thirdHigh << 20) | (1UL << 43));
        c.Emit(0x5c10000000070000UL | destinationLow | (250UL << 8) | (255UL << 20));
        return true;
    }

    // IDP.2A.LO.U16.U8: a.lo16 * b.byte0 + a.hi16 * b.byte1 + c.
    private static bool DotProduct(InstructionContext c)
    {
        if ((c.Insn.W2 & ~0xffU) != 0x1000 || (c.Insn.W1 & ~0xffU) != 0) return c.Fail("native packed dot-product mode");
        uint a = c.Bits(24, 8), b = c.Bits(32, 8), third = c.Bits(64, 8);
        for (uint component = 0; component < 2; component++)
        {
            c.Emit(0x3828000000000000UL | c.Predicate | (250 + component) | ((ulong)b << 8) | ((ulong)(component * 8) << 20));
            c.Emit(0x0400000000000000UL | c.Predicate | (250 + component) | ((ulong)(250 + component) << 8) | (0xffUL << 20));
        }

        c.Integer.Xmad(252, a, 250, third, false, false, 0, false);
        c.Integer.Xmad((uint)c.Dest, a, 251, 252, true, false, 0, false);
        return true;
    }

    private static bool Trap(InstructionContext c)
    {
        IReadOnlyCollection<uint>? traps = c.State.Options.VerifiedTraps;
        if (c.Insn.W0 != 0x795c || c.Insn.W1 != 4 || c.Insn.W2 != 0x00300000 || traps is null || !traps.Contains(c.Index * 16))
            return c.Fail("native runtime breakpoint/trap");
        c.Emit(0x50b0000000000f00UL | c.Predicate);
        return true;
    }

    // IMAD, constant-buffer multiplier.
    private static bool ConstantMultiply(InstructionContext c)
    {
        if ((c.Insn.W2 & ~0x2ffU) != 0x078e0000 || !c.ValidCbuf() || c.Bits(62, 2) != 0) return c.Fail("native integer constant-buffer multiply mode");
        c.Emit(0x4c98000000000000UL | c.Predicate | 252 | (15UL << 39) | c.Cbuf());
        c.Integer.MultiplyLow(c.Bits(24, 8), 252, c.Bits(64, 8), false);
        return true;
    }

    // IMAD.MOV.U32: zero product plus immediate/cbuf.
    private static bool MoveOrMultiply(InstructionContext c)
    {
        if (c.Opcode == 0x624 && c.Bits(24, 8) == 255 && (c.Insn.W2 & ~0x07800000U) == 0x000e06ffU)
        {
            uint source = c.Operands.Operand(2, false);
            c.MovImm(250, 1);
            c.Emit(0x5ca0000000000000UL | c.Predicate | 250 | (250UL << 8) | (255UL << 20) | ((ulong)c.Bits(87, 3) << 39) | ((ulong)c.Bits(90, 1) << 42));
            c.Emit(c.Base(0x5c10000000000000UL) | ((ulong)source << 8) | (250UL << 20));
            return true;
        }

        if (c.Bits(24, 8) != 255 || c.Bits(64, 8) != 255 || c.Insn.W2 != 0x078e00ff)
        {
            if ((c.Insn.W2 & ~0x2ffU) != 0x078e0000 || (c.Opcode == 0x624 && c.Bits(62, 2) != 0)) return c.Fail("native multiply wide addend mode");
            uint a = c.Operands.Operand(0, false), b = c.Operands.Operand(1, false), third = c.Operands.Operand(2, false);
            c.Integer.MultiplyLow(a, b, third, false);
            return true;
        }

        if (c.Opcode == 0x624)
        {
            if (!c.ValidCbuf() || c.Bits(62, 2) != 0) return c.Fail("native integer move constant buffer");
            c.Emit(c.Base(0x4c98000000000000UL) | (15UL << 39) | c.Cbuf());
        }
        else
        {
            c.Emit(c.Base(0x0100000000000000UL) | (15UL << 12) | ((ulong)c.Insn.W1 << 20));
        }

        return true;
    }

    // Captured low-word IMAD forms; bit 75 negates the addend.
    private static bool CapturedMultiply(InstructionContext c)
    {
        bool negateC = c.Bits(75, 1) != 0;
        if (c.Insn.W1 == 1 && (c.Insn.W2 & ~(0x07800800U | 0xffU)) == 0x000e0600U) return AddWithCarry(c, negateC);
        if (c.Insn.W1 == 1 && (c.Insn.W2 & 0xfffff700U) == 0x078e0200)
        {
            c.Emit(c.Base(0x5c10000000000000UL) | ((ulong)c.Bits(24, 8) << 8) | ((ulong)c.Bits(64, 8) << 20) | (InstructionContext.Flag(negateC) << 48));
            return true;
        }

        uint multiplier = c.Insn.W1;
        if (c.Bits(64, 8) == 255 && !negateC && multiplier > 1 && (multiplier & (multiplier - 1)) == 0 && (c.Insn.W2 & ~0x2ffU) == 0x078e0000)
        {
            // IMAD.SHL by a power of two: one shift (also the form the jump-table tracking expects between the clamp and the table load).
            int shift = 0;
            while ((1U << shift) != multiplier) shift++;
            c.Emit(c.Base(0x3848000000000000UL) | ((ulong)c.Bits(24, 8) << 8) | ((ulong)shift << 20));
            return true;
        }

        if (c.Insn.W1 != 48 || c.Insn.W2 != 0x078e02ff) return ImmediateMultiply(c, negateC);
        ulong source = c.Bits(24, 8);
        c.Emit(0x5c18000000000000UL | c.Predicate | 250 | (source << 8) | (source << 20) | (1UL << 39));
        c.Emit(c.Base(0x3848000000000000UL) | (250UL << 8) | (4UL << 20));
        return true;
    }

    // IMAD.X Rd, Ra, 1, Rc, Pn: Ra + Rc plus the carry predicate in bits 87-90 (~Rc with bit 75).
    private static bool AddWithCarry(InstructionContext c, bool negateC)
    {
        uint addend = c.Bits(64, 8);
        c.MovImm(252, 1);
        c.Emit(0x5ca0000000000000UL | c.Predicate | 252 | (252UL << 8) | (255UL << 20) | ((ulong)c.Bits(87, 3) << 39) | ((ulong)c.Bits(90, 1) << 42));
        if (negateC)
        {
            c.Emit(0x0400000000000000UL | c.Predicate | 251 | ((ulong)addend << 8) | (0xffffffffUL << 20) | (2UL << 53));
            addend = 251;
        }

        c.Emit(0x5c10000000000000UL | c.Predicate | 250 | ((ulong)c.Bits(24, 8) << 8) | ((ulong)addend << 20));
        c.Emit(c.Base(0x5c10000000000000UL) | (250UL << 8) | (252UL << 20));
        return true;
    }

    private static bool ImmediateMultiply(InstructionContext c, bool negateC)
    {
        if ((c.Insn.W2 & ~0xaffU) != 0x078e0000) return c.Fail("native integer multiply mode");
        uint addend = c.Bits(64, 8);
        if (negateC && addend != 255)
        {
            c.Emit(0x5c10000000000000UL | c.Predicate | 252 | (255UL << 8) | ((ulong)addend << 20) | (1UL << 48));
            addend = 252;
        }

        c.Integer.MultiplyLow(c.Bits(24, 8), c.Insn.W1, addend, true);
        return true;
    }
}
