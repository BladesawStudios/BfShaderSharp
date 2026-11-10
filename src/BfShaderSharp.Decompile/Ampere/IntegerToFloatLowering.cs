using System.Numerics;

namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers integer-to-float conversions: byte, half-word and word sources, and the constant-folded immediate form.</summary>
/// <remarks>
/// The scalar forms convert a byte or half-word selected from a register (signedness and selector independent). The
/// packed word form maps onto Maxwell I2F with the rounding mode in the same bits. A conversion of an immediate is
/// folded at lowering time when its rounding allows (nearest, or up), and goes through scratch otherwise. Ported from
/// the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class IntegerToFloatLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(Immediate, 0x845, 0x906);
        table.Add(Scalar, 0x306, 0xd06);
        table.Add(Packed, 0x245, 0xa45, 0xc45);
    }

    private static bool Immediate(InstructionContext c)
    {
        bool roundUp = c.Opcode == 0x906;
        if (c.Bits(24, 8) != 0 || (c.Insn.W3 & 0xc00001ffU) != 0 || (c.Insn.W2 & ~0xc400U) != 0x00201000U) return c.Fail("native immediate integer-to-float conversion mode");
        if (c.Insn.W2 != (roundUp ? 0x00209000U : 0x0020d000U))
        {
            c.MovImm(250, c.Insn.W1);
            c.Emit(c.Base(0x5cb8000000000000UL) | (2UL << 8) | (2UL << 10) | ((ulong)c.Bits(74, 1) << 13) | (250UL << 20) | ((ulong)c.Bits(78, 2) << 39));
            return true;
        }

        uint value = c.Insn.W1, converted = 0;
        if (value != 0)
        {
            int shift = BitOperations.LeadingZeroCount(value);
            uint normalized = value << shift;
            converted = ((uint)(127 + 31 - shift) << 23) | ((normalized >> 8) & 0x7fffffU);
            if (roundUp && (normalized & 255U) != 0) converted++;
        }

        c.MovImm((uint)c.Dest, converted);
        return true;
    }

    // I2F.F32 from U8/S8, U16/S16 or the scalar byte forms (0xd06: uniform source).
    private static bool Scalar(InstructionContext c)
    {
        uint opcode = c.Opcode, word = c.Insn.W2;
        uint source = opcode == 0xd06 ? c.UniformRegister(c.Bits(32, 8)) : c.Bits(32, 8);
        if (opcode == 0x306 && (word == 0x00200c00 || word == 0x00209400))
        {
            bool half = word == 0x00200c00;
            if (c.Bits(24, 8) != 0 || (c.Insn.W1 & ~0xffU) != 0 || (c.Insn.W3 & 0xc00001ffU) != 0) return c.Fail("native scalar integer-to-float conversion mode");
            c.Emit(c.Base(0x5cb8000000000000UL) | ((half ? 1UL : 2UL) << 8) | (2UL << 10) | (1UL << 13) | ((ulong)source << 20) | ((half ? 0UL : 2UL) << 39));
            return true;
        }

        bool halfWord = (opcode == 0x306 && (word & ~0x400U) == 0x00101000) ||
                        (opcode == 0xd06 && word == 0x08101000U && c.Bits(61, 1) == 0 && c.Bits(24, 8) == 0 && (c.Insn.W3 & 0xc00001ffU) == 0);
        if (halfWord && (c.Insn.W1 & 0xcfffff00U) == 0 && source != uint.MaxValue)
        {
            // I2F.U16/S16 Rd, Rs(.H1): signedness and the halfword selector are independent.
            c.Emit(c.Base(0x5cb8000000000000UL) | (2UL << 8) | (1UL << 10) | ((ulong)c.Bits(74, 1) << 13) | ((ulong)source << 20) | ((ulong)(c.Bits(60, 2) * 2) << 41));
            return true;
        }

        // I2F.{U,S}8 Rd, Rs.B0-B3: the byte selector (bits 60-61) as Maxwell's (41-42).
        if ((word & ~0x400U) != (opcode == 0xd06 ? 0x08001000U : 0x1000U) || (c.Insn.W1 & 0xcfffff00U) != 0 || source == uint.MaxValue)
            return c.Fail("native byte-to-float conversion mode");
        c.Emit(c.Base(0x5cb8000000000000UL) | (2UL << 8) | ((ulong)c.Bits(74, 1) << 13) | ((ulong)source << 20) | ((ulong)c.Bits(60, 2) << 41));
        return true;
    }

    // I2FP.F32.U32; bits 78-79 are the rounding mode (RN/RM/RP/RZ), as Maxwell I2F's bits 39-40.
    private static bool Packed(InstructionContext c)
    {
        if ((c.Insn.W2 & ~(0x400U | 0xc000U | (c.Opcode == 0xc45 ? 0x08000000U : 0U))) != 0x00201000) return c.Fail("native packed integer-to-float format");
        uint source = c.Operands.Operand(1, false);
        c.Emit(c.Base(0x5cb8000000000000UL) | (2UL << 8) | (2UL << 10) | ((ulong)c.Bits(74, 1) << 13) | ((ulong)source << 20) | ((ulong)c.Bits(78, 2) << 39));
        return true;
    }
}
