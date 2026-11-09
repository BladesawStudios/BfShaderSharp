namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers register moves (MOV from a register, immediate, constant buffer or uniform register), the half-word permute (PRMT) and the predicate-register conversions (P2R, R2P).</summary>
/// <remarks>
/// MOV forms map onto Maxwell's move, with the uniform source taken from its mirror register. PRMT is kept to the
/// half-word concatenations the compiler emits as two masks and a merge; other selectors are done byte by byte, each
/// byte extracted and inserted through scratch. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class MoveLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(Move, 0xc02, 0x202, 0x802, 0xa02);
        table.Add(Permute, 0x816);
        table.Add(PredicateToRegister, 0x803);
        table.Add(RegisterToPredicate, 0x804);
    }

    private static bool Move(InstructionContext c)
    {
        uint opcode = c.Opcode;
        if (c.Bits(72, 4) != 15 || (c.Bits(76, 16) & ~(opcode == 0xc02 ? 0x8000U : 0U)) != 0) return c.Fail("native MOV modifiers");
        if (opcode == 0x802)
        {
            c.Emit(c.Base(0x0100000000000000UL) | (15UL << 12) | ((ulong)c.Insn.W1 << 20));
        }
        else if (opcode == 0xc02)
        {
            // Any uniform register.
            uint source = c.Insn.W1 < 64 ? c.UniformRegister(c.Insn.W1) : uint.MaxValue;
            if (source == uint.MaxValue || c.Insn.W2 != 0x08000f00) return c.Fail("native uniform move source");
            c.Emit(c.Base(0x5c98000000000000UL) | (15UL << 39) | ((ulong)source << 20));
        }
        else if (opcode == 0x202)
        {
            if (c.Bits(40, 24) != 0) return c.Fail("native MOV register modifiers");
            c.Emit(c.Base(0x5c98000000000000UL) | (15UL << 39) | ((ulong)c.Bits(32, 8) << 20));
        }
        else
        {
            if (!c.ValidCbuf() || c.Bits(62, 2) != 0) return c.Fail("native MOV constant buffer");
            c.Emit(c.Base(0x4c98000000000000UL) | (15UL << 39) | c.Cbuf());
        }

        return true;
    }

    private static bool Permute(InstructionContext c)
    {
        uint selector = c.Insn.W1;
        if (selector > 0xffff || (c.Insn.W2 & ~0xffU) != 0) return c.Fail("native permutation mode");
        if (selector != 0x5410 && selector != 0x5432 && selector != 0x7610 && selector != 0x7632) return ByteWise(c);
        uint a = c.Bits(24, 8), b = c.Bits(64, 8);
        if (selector != 0x5432 && selector != 0x7632) c.Emit(0x0400000000000000UL | c.Predicate | 250 | ((ulong)a << 8) | (0xffffUL << 20));
        else c.Emit(0x3828000000000000UL | c.Predicate | 250 | ((ulong)a << 8) | (16UL << 20));
        if (selector == 0x7610 || selector == 0x7632)
        {
            c.Emit(0x0400000000000000UL | c.Predicate | 251 | ((ulong)b << 8) | (0xffff0000UL << 20));
            c.Emit(c.Base(0x5c10000000000000UL) | (250UL << 8) | (251UL << 20));
        }
        else
        {
            c.Emit(c.Base(0x5c18000000000000UL) | ((ulong)b << 8) | (250UL << 20) | (16UL << 39));
        }

        return true;
    }

    // Byte i of the result is byte (s & 7) of c:a, or its sign bit repeated when s & 8.
    private static bool ByteWise(InstructionContext c)
    {
        uint a = c.Bits(24, 8), high = c.Bits(64, 8);
        if (a >= 250 && a != 255) return c.Fail("native permutation source");
        c.MovImm(250, 0);
        for (uint i = 0; i < 4; i++)
        {
            uint select = (c.Insn.W1 >> (int)(i * 4)) & 0xf;
            uint source = (select & 4) != 0 ? high : a, position = (select & 3) * 8;
            uint field = (select & 8) != 0 ? (position + 7) | (1U << 8) : position | (8U << 8);
            c.Emit(0x3800000000000000UL | c.Predicate | 251 | ((ulong)source << 8) | ((ulong)field << 20) | ((ulong)((select >> 3) & 1) << 48));
            c.Emit(0x36f0000000000000UL | c.Predicate | 250 | (251UL << 8) | ((ulong)((i * 8) | (8U << 8)) << 20) | (250UL << 39));
        }

        c.Emit(c.Base(0x5c10000000000000UL) | (250UL << 8) | (255UL << 20));
        return true;
    }

    // P2R PR, register merge source, immediate mask, byte select (bits 76-77).
    private static bool PredicateToRegister(InstructionContext c)
    {
        if (c.Insn.W1 > 0x7f || (c.Insn.W2 & ~0x3000U) != 0) return c.Fail("native predicate-to-register mode");
        c.Emit(c.Base(0x38e8000000000000UL) | ((ulong)c.Bits(24, 8) << 8) | ((ulong)c.Insn.W1 << 20) | ((ulong)c.Bits(76, 2) << 41));
        return true;
    }

    // R2P PR, register, immediate mask, byte select (bits 76-77).
    private static bool RegisterToPredicate(InstructionContext c)
    {
        if (c.Bits(16, 8) != 0 || c.Insn.W1 > 0x7f || (c.Insn.W2 & ~0x3000U) != 0) return c.Fail("native register-to-predicate mode");
        c.Emit(0x38f0000000000000UL | c.Predicate | ((ulong)c.Bits(24, 8) << 8) | ((ulong)c.Insn.W1 << 20) | ((ulong)c.Bits(76, 2) << 41));
        return true;
    }
}
