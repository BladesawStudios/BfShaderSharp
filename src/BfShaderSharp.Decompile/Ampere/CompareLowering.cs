namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the floating-point set (FSET), packed-half compare (HSETP2) and the float and integer predicate compares (FSETP, ISETP).</summary>
/// <remarks>
/// The extended integer compare (ISETP.EX) is the high-word half of a 64-bit compare: the low words' result arrives as
/// a predicate, and the high words combine with it: equal and low, different or low, or strictly ordered or (equal
/// and low). It is done in a free scratch predicate, saved and restored around the sequence. Ported from the Eden
/// emulator (GPL-3.0-or-later).
/// </remarks>
internal static class CompareLowering
{
    private const uint And = 0, Or = 1, LessThan = 1, Equal = 2, GreaterThan = 4, NotEqual = 5;

    public static void Register(OpcodeTable table)
    {
        table.Add(FloatSet, 0x20a, 0x80a, 0xa0a, 0xc0a);
        table.Add(HalfCompare, 0x234, 0x434);
        table.Add(SetPredicate, 0x20b, 0x80b, 0xa0b, 0xc0b, 0x20c, 0x80c, 0xa0c, 0xc0c);
    }

    // FSET.BF, combined with Pc (bits 87-90) by bits 74-75 (0xc0a: uniform b).
    private static bool FloatSet(InstructionContext c)
    {
        if (c.Bits(64, 8) != 0 || c.Bits(74, 2) == 3 || c.Bits(81, 6) != 0 || (c.Opcode != 0xc0a && c.Bits(91, 1) != 0)) return c.Fail("native floating comparison output mode");
        uint a = c.Operands.Operand(0), b = c.Operands.Operand(1);
        c.Emit(c.Base(0x5800000000000000UL) | ((ulong)a << 8) | ((ulong)b << 20) | ((ulong)c.Bits(87, 4) << 39) | ((ulong)c.Bits(74, 2) << 45) |
               ((ulong)c.Bits(76, 4) << 48) | (1UL << 52) | ((ulong)c.Bits(80, 1) << 55));
        return true;
    }

    // HSETP2 with a register (B swizzle 60-61, abs 62, neg 63) or an immediate half pair; compare 76-79, FTZ 80, P/Q 81-86, input 87-90.
    private static bool HalfCompare(InstructionContext c)
    {
        ulong source = 250UL << 20;
        if (c.Opcode == 0x234)
        {
            if ((c.Insn.W1 & 0x0fffff00U) != 0 || c.Bits(64, 5) != 0) return c.Fail("native half comparison mode");
            source = ((ulong)c.Bits(32, 8) << 20) | ((ulong)c.Bits(60, 2) << 28) | ((ulong)c.Bits(62, 1) << 30) | ((ulong)c.Bits(63, 1) << 31);
        }
        else
        {
            c.MovImm(250, c.Insn.W1);
        }

        c.Emit(0x5d20000000000000UL | c.Predicate | c.Bits(84, 3) | ((ulong)c.Bits(81, 3) << 3) | ((ulong)c.Bits(80, 1) << 6) | ((ulong)c.Bits(24, 8) << 8) | source |
               ((ulong)c.Bits(76, 4) << 35) | ((ulong)c.Bits(87, 3) << 39) | ((ulong)c.Bits(90, 1) << 42) | ((ulong)c.Bits(72, 1) << 43) |
               ((ulong)c.Bits(73, 1) << 44) | ((ulong)c.Bits(69, 2) << 45) | ((ulong)c.Bits(74, 2) << 47) | ((ulong)c.Bits(71, 1) << 49));
        return true;
    }

    private static bool SetPredicate(InstructionContext c)
    {
        bool floating = (c.Opcode & 0x1ff) == 0xb;
        if (!floating && c.Bits(72, 1) != 0) return Extended(c);
        uint a = c.Operands.Operand(0, floating), b = c.Operands.Operand(1, floating), combine = c.Bits(74, 2);
        if (combine > 2) return c.Fail("native predicate combination");
        ulong word = (floating ? 0x5bb0000000000000UL : 0x5b60000000000000UL) | c.Predicate | c.Bits(84, 3) | ((ulong)c.Bits(81, 3) << 3) | ((ulong)a << 8) |
                     ((ulong)b << 20) | ((ulong)c.Bits(87, 3) << 39) | ((ulong)c.Bits(90, 1) << 42) | ((ulong)combine << 45);
        word |= floating ? ((ulong)c.Bits(80, 1) << 47) | ((ulong)c.Bits(76, 4) << 48) : ((ulong)c.Bits(73, 1) << 48) | ((ulong)c.Bits(76, 3) << 49);
        c.Emit(word);
        return true;
    }

    private static ulong Compare(InstructionContext c, uint a, uint b, uint temporary, uint op, uint input, uint combine) =>
        0x5b60000000000000UL | c.Predicate | 7 | ((ulong)temporary << 3) | ((ulong)a << 8) | ((ulong)b << 20) | ((ulong)(input & 7) << 39) |
        ((ulong)(input >> 3) << 42) | ((ulong)combine << 45) | ((ulong)c.Bits(73, 1) << 48) | ((ulong)op << 49);

    private static bool Extended(InstructionContext c)
    {
        uint compare = c.Bits(76, 3), destination = c.Bits(81, 3), destinationB = c.Bits(84, 3), low = c.Bits(68, 4), accumulate = c.Bits(87, 4), combine = c.Bits(74, 2);
        if (compare == 0 || compare == 7 || combine == 3) return c.Fail("native extended integer comparison mode");
        uint a = c.Operands.Operand(0, false), b = c.Operands.Operand(1, false), temporary = 0;
        while (temporary == destination || temporary == destinationB || temporary == (low & 7) || temporary == (accumulate & 7) || temporary == c.Bits(12, 3)) temporary++;
        uint mask = 1U << (int)temporary;
        c.Emit(0x38e8000000070000UL | 253 | (255UL << 8) | ((ulong)mask << 20));
        if (compare == Equal)
        {
            c.Emit(Compare(c, a, b, temporary, Equal, low, And));
        }
        else if (compare == NotEqual)
        {
            c.Emit(Compare(c, a, b, temporary, NotEqual, low, Or));
        }
        else
        {
            c.Emit(Compare(c, a, b, temporary, Equal, low, And));
            c.Emit(Compare(c, a, b, temporary, compare <= 3 ? LessThan : GreaterThan, temporary, Or));
        }

        c.Emit(0x5090000000000000UL | c.Predicate | destinationB | ((ulong)destination << 3) | ((ulong)temporary << 12) | (7UL << 29) | ((ulong)(accumulate & 7) << 39) |
               ((ulong)(accumulate >> 3) << 42) | ((ulong)combine << 45));
        c.Emit(0x38f0000000070000UL | (253UL << 8) | ((ulong)mask << 20));
        return true;
    }
}
