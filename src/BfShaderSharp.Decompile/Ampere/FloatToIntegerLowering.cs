namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the float-to-integer conversions (F2I, 32 and 64 bit) and the 64-bit integer-to-float conversion.</summary>
/// <remarks>
/// The conversion mode bits select the destination width and rounding. Unsigned results are made to saturate: NaNs are
/// cleared and the source clamped below 2^32 with an overflow mask, which a final select applies. Signed half-width
/// conversions clamp against the 16-bit range in scratch registers, and byte conversions clamp to the byte range
/// first. A packed float-to-byte form is rewritten as the plain word form before it is lowered. Ported from the Eden
/// emulator (GPL-3.0-or-later).
/// </remarks>
internal static class FloatToIntegerLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(UnsignedLong, 0x311);
        table.Add(LongToFloat, 0x312);
        table.Add(Packed, 0x243);
        table.Add(Convert, 0x305, 0xb05, 0xd05);
    }

    private sealed class Mode
    {
        public bool SignedHalf, Half, Small, ShortTruncate, Absolute, Negate, Byte, Signed;
    }

    private static bool UnsignedLong(InstructionContext c)
    {
        if (c.Insn.W2 != 0x0021d800 || (c.Insn.W1 & ~0xffU) != 0 || c.Bits(24, 8) != 0 || (c.Dest & 1) != 0 || c.Dest >= 246)
            return c.Fail("native unsigned 64-bit float conversion mode");
        c.Emit(c.Base(0x5cb0000000000000UL) | (3UL << 8) | (2UL << 10) | ((ulong)c.Bits(32, 8) << 20) | (3UL << 39) | (1UL << 44));
        return true;
    }

    private static bool LongToFloat(InstructionContext c)
    {
        if ((c.Insn.W2 & ~0xc400U) != 0x00301000 || (c.Insn.W1 & ~0xffU) != 0 || c.Bits(24, 8) != 0 || (c.Bits(32, 8) & 1) != 0 || c.Bits(32, 8) >= 254)
            return c.Fail("native 64-bit integer-to-float mode");
        c.Emit(c.Base(0x5cb8000000000000UL) | (2UL << 8) | (3UL << 10) | ((ulong)c.Bits(74, 1) << 13) | ((ulong)c.Bits(32, 8) << 20) | ((ulong)c.Bits(78, 2) << 39));
        return true;
    }

    // F2I with a packed byte result: rewritten as the word form, then lowered as that.
    private static bool Packed(InstructionContext c)
    {
        if (c.Bits(24, 8) != 255 || (c.Insn.W1 & ~0xffU) != 0 || c.Insn.W2 != 0x0000c4ffU) return c.Fail("native packed float-to-byte mode");
        c.Insn.W0 &= 0x00ffffffU;
        c.Insn.W2 = 0x0021e000U;
        c.Opcode = 0x305;
        return Convert(c);
    }

    private static Mode Classify(InstructionContext c, uint word)
    {
        uint opcode = c.Opcode;
        var mode = new Mode
        {
            SignedHalf = opcode == 0x305 && (word == 0x00103100 || word == 0x00107100),
            Small = (word & ~0xc000U) == 0x00212800,
            ShortTruncate = opcode == 0x305 && (word & ~0x100U) == 0x0021e800,
            Byte = (word & ~0x10100U) == 0x0020e000,
            Signed = c.Bits(72, 1) != 0,
        };
        mode.Half = mode.SignedHalf || (opcode == 0x305 && (word & ~0x100U) == 0x0010f000);
        mode.Absolute = opcode == 0x305 && !mode.Half && c.Bits(62, 1) != 0;
        mode.Negate = opcode == 0x305 && !mode.Half && c.Bits(63, 1) != 0;
        return mode;
    }

    private static bool Supported(InstructionContext c, Mode mode, uint word)
    {
        uint opcode = c.Opcode;
        bool wide = (word & ~0x1c100U) == 0x00203000;
        bool extendedWord = wide && (c.Bits(78, 2) == 2 || (c.Bits(78, 2) == 3 && c.Bits(80, 1) == 0));
        bool other = !mode.Half && !mode.Small && !mode.ShortTruncate && !mode.Byte && !wide;
        bool narrow = mode.Byte || mode.Small || mode.ShortTruncate || extendedWord;
        bool badNarrow = narrow && (c.Bits(24, 8) != 0 || (c.Insn.W3 & 0xc00001ffU) != 0 ||
            (opcode == 0xb05 && (c.Bits(63, 1) != 0 || (!mode.Small && c.Bits(62, 1) != 0) || (mode.Small && c.Bits(59, 3) != 0))) ||
            c.Bits(91, 1) != (opcode == 0xd05 ? 1U : 0U));
        uint allowed = mode.Half ? 0x100000ffU : 0xffU | (mode.Absolute ? 0x40000000U : 0U) | (mode.Negate ? 0x80000000U : 0U);
        return !(other || (mode.SignedHalf && (c.Bits(24, 8) != 0 || (c.Insn.W3 & 0xc00001ffU) != 0)) || badNarrow ||
                 (opcode != 0xb05 && (c.Insn.W1 & ~allowed) != 0) || (opcode == 0xd05 && c.UniformRegister(c.Bits(32, 8)) == uint.MaxValue));
    }

    private static bool Convert(InstructionContext c)
    {
        uint opcode = c.Opcode;
        uint word = c.Insn.W2 & ~(opcode == 0xd05 ? 0x08000000U : 0U);
        Mode mode = Classify(c, word);
        if (!Supported(c, mode, word)) return c.Fail("native float-to-integer conversion mode");
        OperandBuilder operands = c.Operands;
        uint source = opcode == 0xb05 ? operands.Materialize(2, 0, 252, mode.Small && c.Bits(62, 1) != 0, false)
                    : opcode == 0xd05 ? c.UniformRegister(c.Bits(32, 8)) : c.Bits(32, 8);
        if (mode.Half)
        {
            operands.ExtractHalf(new HalfSource(source, c.Bits(60, 1) != 0 ? 3U : 2U, false, false), 0, 252);
            source = 252;
        }

        if (mode.Absolute || mode.Negate) source = operands.Materialize(0, source, 249, mode.Absolute, mode.Negate);
        if (mode.SignedHalf)
        {
            SaturatingHalf(c);
            return true;
        }

        if (mode.Byte) source = ClampByte(c, source, mode.Signed);
        else if (!mode.Signed) source = ClampUnsigned(c, source, mode);
        bool narrowResult = mode.Small || mode.ShortTruncate;
        c.Emit(c.Base(0x5cb0000000000000UL) | ((narrowResult ? 1UL : 2UL) << 8) | (2UL << 10) | (InstructionContext.Flag(mode.Signed) << 12) |
               ((ulong)source << 20) | ((ulong)c.Bits(78, 2) << 39) | ((ulong)c.Bits(80, 1) << 44));
        if (!mode.Signed && !narrowResult && !mode.Byte)
            c.Emit(c.Base(0x5c40000000000000UL) | (c.Dest << 8) | (251UL << 20) | (1UL << 41) | (7UL << 48));
        return true;
    }

    // Float-to-signed-16 with saturation at both ends, built from compares against +-2^16 in scratch registers.
    private static void SaturatingHalf(InstructionContext c)
    {
        ulong p = c.Predicate;
        c.MovImm(253, 0x47800000);
        c.Emit(0x5800000000000000UL | p | 250 | (252UL << 8) | (253UL << 20) | (7UL << 39) | (1UL << 48) | (1UL << 54));
        c.Emit(0x5800000000000000UL | p | 251 | (252UL << 8) | (253UL << 20) | (7UL << 39) | (4UL << 48));
        c.MovImm(253, 0xc7800000);
        c.Emit(0x5800000000000000UL | p | 253 | (252UL << 8) | (253UL << 20) | (7UL << 39) | (1UL << 48));
        c.Emit(0x5c40000000000000UL | p | 252 | (252UL << 8) | (250UL << 20) | (7UL << 48));
        c.Emit(c.Base(0x5cb0000000000000UL) | (2UL << 8) | (2UL << 10) | (1UL << 12) | (252UL << 20) | ((ulong)c.Bits(78, 2) << 39));
        c.MovImm(250, 0x7fffffff);
        c.Emit(0x5c40000000000000UL | p | 251 | (251UL << 8) | (250UL << 20) | (7UL << 48));
        c.MovImm(250, 0x80000000);
        c.Emit(0x5c40000000000000UL | p | 253 | (253UL << 8) | (250UL << 20) | (7UL << 48));
        c.Emit(c.Base(0x5c40000000000000UL) | (c.Dest << 8) | (251UL << 20) | (1UL << 41) | (7UL << 48));
        c.Emit(c.Base(0x5c40000000000000UL) | (c.Dest << 8) | (253UL << 20) | (1UL << 41) | (7UL << 48));
    }

    // Clears NaNs and clamps to the byte range (signed -128..127, unsigned 0..255) in R250.
    private static uint ClampByte(InstructionContext c, uint source, bool signed)
    {
        ulong p = c.Predicate;
        c.Emit(0x5800000000000000UL | p | 250 | ((ulong)source << 8) | ((ulong)source << 20) | (7UL << 39) | (7UL << 48));
        c.Emit(0x5c40000000000000UL | p | 250 | ((ulong)source << 8) | (250UL << 20) | (7UL << 48));
        c.MovImm(252, signed ? 0xc3000000U : 0U);
        c.Emit(0x5c60000000000000UL | p | 250 | (250UL << 8) | (252UL << 20) | (15UL << 39));
        c.MovImm(252, signed ? 0x42fe0000U : 0x437f0000U);
        c.Emit(0x5c60000000000000UL | p | 250 | (250UL << 8) | (252UL << 20) | (7UL << 39));
        return 250;
    }

    // Clears NaNs without consuming a guest predicate, then clamps below 2^32, keeping an overflow mask in R251.
    private static uint ClampUnsigned(InstructionContext c, uint source, Mode mode)
    {
        ulong p = c.Predicate;
        c.Emit(0x5800000000000000UL | p | 250 | ((ulong)source << 8) | ((ulong)source << 20) | (7UL << 39) | (7UL << 48));
        c.Emit(0x5c40000000000000UL | p | 250 | ((ulong)source << 8) | (250UL << 20) | (7UL << 48));
        if (!mode.Small && !mode.ShortTruncate)
        {
            c.MovImm(252, 0x4f800000);
            c.Emit(0x5800000000000000UL | p | 251 | (250UL << 8) | (252UL << 20) | (7UL << 39) | (6UL << 48));
            c.MovImm(252, 0x4f7fffff);
            c.Emit(0x5c60000000000000UL | p | 250 | (250UL << 8) | (252UL << 20) | (7UL << 39));
        }

        return 250;
    }
}
