namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the half-precision arithmetic, comparison and conversion instructions.</summary>
/// <remarks>
/// Covers HMUL2, HADD2, HFMA2 (with its ReLU form), HSET2, HMNMX2, the scalar F2F to half conversion and the F2FP
/// pack. Each maps onto the Maxwell packed-half instruction of the same operation, or onto a short sequence when
/// Ampere has a modifier Maxwell lacks. Modes the sequences do not cover are rejected. Ported from the Eden emulator
/// (GPL-3.0-or-later).
/// </remarks>
internal static class HalfFloatLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(ScalarConversion, 0x304);
        table.Add(UniformPack, 0xc3e);
        table.Add(Pack, 0x23e, 0xa3e);
        table.Add(Multiply, 0x232, 0x832, 0xa32);
        table.Add(Add, 0x230, 0x430, 0x630);
        table.Add(FusedMultiplyAdd, 0x231, 0x431, 0x631, 0x831, 0xa31);
        table.Add(Compare, 0x233, 0x433, 0x633);
        table.Add(MinMax, 0x240, 0x840);
    }

    // F2F.F16.F32, scalar round-to-nearest conversion.
    private static bool ScalarConversion(InstructionContext c)
    {
        if (c.Insn.W2 != 0x00200800 || c.Bits(24, 8) != 0 || (c.Insn.W1 & ~0xc00000ffU) != 0) return c.Fail("native scalar float conversion mode");
        c.Emit(c.Base(0x5ca8000000000000UL) | (1UL << 8) | (2UL << 10) | ((ulong)c.Bits(32, 8) << 20) | ((ulong)c.Bits(62, 1) << 49) | ((ulong)c.Bits(63, 1) << 45));
        return true;
    }

    private static bool UniformPack(InstructionContext c)
    {
        uint source = c.UniformRegister(c.Bits(32, 8));
        if (c.Bits(24, 8) != 255 || (c.Insn.W1 & ~0xffU) != 0 || c.Insn.W2 != 0x080000ff || (c.Insn.W3 & 0xc00001ffU) != 0 || source == uint.MaxValue)
            return c.Fail("native uniform float packing mode");
        c.Emit(c.Base(0x5ca8000000000000UL) | (1UL << 8) | (2UL << 10) | ((ulong)source << 20));
        c.Emit(0x5800000000000000UL | c.Predicate | 250 | ((ulong)source << 8) | ((ulong)source << 20) | (7UL << 39) | (8UL << 48));
        c.MovImm(251, 0x7e00);
        c.Emit(0x5c40000000000000UL | c.Predicate | 250 | (250UL << 8) | (251UL << 20) | (7UL << 48));
        c.Emit(c.Base(0x5c40000000000000UL) | (c.Dest << 8) | (250UL << 20) | (1UL << 41) | (7UL << 48));
        return true;
    }

    // F2FP.PACK_AB / MERGE_C, round-to-nearest.
    private static bool Pack(InstructionContext c)
    {
        if (c.Bits(62, 2) != 0 || c.Bits(73, 5) != 0 || c.Bits(79, 2) != 0 || c.Bits(81, 10) != 0 || c.Bits(91, 1) != 0) return c.Fail("native float packing modifiers");
        bool merge = c.Bits(78, 1) != 0;
        if (c.Bits(72, 1) != 0) return c.Fail("native float packing selector");
        uint a = merge ? c.Operands.Operand(2, false) : c.Operands.Operand(0, false);
        uint b = c.Operands.Operand(1, false);
        // Canonical F2F writes the converted half in its low 16 bits.
        if (merge) c.Emit(0x5c98000000000000UL | c.Predicate | 250 | ((ulong)a << 20) | (15UL << 39));
        else c.Emit(0x5ca8000000000000UL | c.Predicate | 250 | (1UL << 8) | (2UL << 10) | ((ulong)a << 20));
        c.Emit(0x5ca8000000000000UL | c.Predicate | 251 | (1UL << 8) | (2UL << 10) | ((ulong)b << 20));
        c.Emit(c.Base(0x5c18000000000000UL) | (250UL << 8) | (251UL << 20) | (16UL << 39));
        return true;
    }

    private static bool Multiply(InstructionContext c)
    {
        if (c.Bits(78, 2) != 0 || c.Bits(81, 10) != 0 || c.Bits(91, 1) != 0) return c.Fail("native half multiply mode");
        HalfSource a = c.Operands.Half(0), b = c.Operands.Half(1);
        uint precision = c.Bits(76, 1) != 0 ? 2 : c.Bits(80, 1);
        c.Emit(c.Base(0x5d08000000000000UL) | ((ulong)a.Register << 8) | ((ulong)b.Register << 20) | ((ulong)b.Swizzle << 28) |
               (InstructionContext.Flag(b.Absolute) << 30) | (InstructionContext.Flag(a.Negate != b.Negate) << 31) | ((ulong)c.Bits(77, 1) << 32) |
               ((ulong)precision << 39) | (InstructionContext.Flag(a.Absolute) << 44) | ((ulong)a.Swizzle << 47));
        return true;
    }

    private static bool Add(InstructionContext c)
    {
        if (c.Bits(76, 1) != 0 || c.Bits(79, 1) != 0 || c.Bits(81, 10) != 0 || c.Bits(91, 1) != 0) return c.Fail("native half add mode");
        HalfSource a = c.Operands.Half(0);
        HalfSource b = c.Operands.Half(c.Opcode == 0x230 ? 1u : 2u);
        if (c.Bits(78, 1) != 0)
        {
            if (a.Register != 255 || !a.Negate || a.Absolute || a.Swizzle != 0 || b.Swizzle < 2 || c.Bits(77, 1) != 0 || c.Bits(80, 1) != 0)
                return c.Fail("native float-output half add mode");
            c.Operands.ExtractHalf(b, 0, (uint)c.Dest);
            return true;
        }

        c.Emit(c.Base(0x5d10000000000000UL) | ((ulong)a.Register << 8) | ((ulong)b.Register << 20) | ((ulong)b.Swizzle << 28) |
               (InstructionContext.Flag(b.Absolute) << 30) | (InstructionContext.Flag(b.Negate) << 31) | ((ulong)c.Bits(77, 1) << 32) |
               ((ulong)c.Bits(80, 1) << 39) | (InstructionContext.Flag(a.Negate) << 43) | (InstructionContext.Flag(a.Absolute) << 44) | ((ulong)a.Swizzle << 47));
        return true;
    }

    private static bool FusedMultiplyAdd(InstructionContext c)
    {
        bool relu = c.Bits(79, 1) != 0;
        // With .RELU, bits 87-90 are a predicate operand: the clamp applies only where it holds.
        if (c.Bits(78, 1) != 0 || c.Bits(85, 1) != 0 || c.Bits(91, 1) != 0 || (relu ? c.Bits(77, 1) != 0 : c.Bits(87, 4) != 0))
            return c.Fail("native half fused multiply-add mode");
        if (relu && c.NativePredicate != 7)
        {
            // Guard the whole expansion before its temporary P6 updates.
            c.State.Relocations.Add(new Relocation((uint)c.Output.Count, c.Index + 1));
            c.Emit(0xe24000000000000fUL | ((ulong)(c.NativePredicate ^ 8U) << 16));
            c.Predicate = 7UL << 16;
        }

        HalfSource a = c.Operands.Half(0), b = c.Operands.Half(1), third = c.Operands.Half(2);
        if (c.Bits(86, 1) != 0)
        {
            if (b.Swizzle != 0 || b.Absolute || b.Register >= 250) return c.Fail("native half mixed-sign source mode");
            // H0_NH1 swaps the FP16 halves and negates the new low half.
            c.Emit(0x36f8000000000000UL | c.Predicate | 251 | ((ulong)b.Register << 8) | (16UL << 20) | ((ulong)b.Register << 39));
            c.Emit(0x0400000000000000UL | c.Predicate | 251 | (251UL << 8) | (0x8000UL << 20) | (2UL << 53));
            b = b with { Register = 251 };
        }

        if (b.Absolute || third.Absolute || (a.Absolute && a.Register >= 248)) return c.Fail("native half fused multiply-add absolute source");
        if (a.Absolute)
        {
            c.Emit(0x0400000000000000UL | c.Predicate | 250 | ((ulong)a.Register << 8) | ((a.Swizzle == 1 ? 0x7fffffffUL : 0x7fff7fffUL) << 20));
            a = a with { Register = 250, Absolute = false };
        }

        uint precision = c.Bits(76, 1) != 0 ? 2 : c.Bits(80, 1);
        c.Emit(0x5d00000000000000UL | c.Predicate | (relu ? 254U : c.Dest) | ((ulong)a.Register << 8) | ((ulong)b.Register << 20) | ((ulong)b.Swizzle << 28) |
               (InstructionContext.Flag(third.Negate) << 30) | (InstructionContext.Flag(a.Negate != b.Negate) << 31) | ((ulong)c.Bits(77, 1) << 32) |
               ((ulong)third.Swizzle << 35) | ((ulong)precision << 37) | ((ulong)third.Register << 39) | ((ulong)a.Swizzle << 47));
        if (relu) Relu(c);
        return true;
    }

    // Ampere's trailing predicate enables ReLU; it is not a predicate output. The ordinary FMA result is kept when it is false, including negative zero.
    private static void Relu(InstructionContext c)
    {
        uint reluPredicate = c.Bits(87, 4);
        if (reluPredicate != 7)
        {
            c.Emit(c.Base(0x5c98000000000000UL) | (254UL << 20) | (15UL << 39));
            c.State.Relocations.Add(new Relocation((uint)c.Output.Count, c.Index + 1));
            c.Emit(0xe24000000000000fUL | ((ulong)(reluPredicate ^ 8U) << 16));
        }

        // Clamp the rounded binary16 lanes, preserving positive infinity and canonicalizing either sign of NaN without saturating to one.
        c.SavePredicate();
        for (uint lane = 0; lane < 2; lane++) ClampLane(c, lane);
        c.RestorePredicate();
    }

    private static void ClampLane(InstructionContext c, uint lane)
    {
        c.Emit(0x3828000000070000UL | 251 | (254UL << 8) | ((ulong)(lane * 16) << 20));
        c.Emit(0x0400000000070000UL | 251 | (251UL << 8) | (0xffffUL << 20));
        c.Emit(0x0400000000070000UL | 252 | (251UL << 8) | (0x7fffUL << 20));
        c.MovImm(250, 0x7c00);
        c.Emit(0x5b60000000070000UL | 7 | (6UL << 3) | (250UL << 8) | (252UL << 20) | (7UL << 39) | (1UL << 49));
        c.Emit(0x3828000000070000UL | 252 | (251UL << 8) | (15UL << 20));
        c.MovImm(250, 1);
        c.Emit(0x5c10000000070000UL | 252 | (252UL << 8) | (250UL << 20) | (1UL << 48));
        c.Emit(0x5c40000000070000UL | 251 | (251UL << 8) | (252UL << 20) | (7UL << 48));
        c.MovImm(250, 0x7fff);
        c.Emit(0x5ca0000000070000UL | 251 | (250UL << 8) | (251UL << 20) | (6UL << 39));
        if (lane == 0)
        {
            c.Emit(c.Base(0x5c98000000000000UL) | (251UL << 20) | (15UL << 39));
            return;
        }

        c.Emit(0x3848000000070000UL | 251 | (251UL << 8) | (16UL << 20));
        c.Emit(c.Base(0x5c40000000000000UL) | (c.Dest << 8) | (251UL << 20) | (1UL << 41) | (7UL << 48));
    }

    // HSET2, packed half comparisons and predicate combination.
    private static bool Compare(InstructionContext c)
    {
        uint combine = c.Bits(69, 2);
        if (c.Bits(64, 5) != 0 || combine > 2 || c.Bits(81, 6) != 0 || c.Bits(91, 1) != 0 || (c.Opcode == 0x233 && (c.Insn.W1 & 0x0fffff00U) != 0))
            return c.Fail("native packed half comparison mode");
        HalfSource a = c.Operands.Half(0), b = c.Operands.Half(c.Opcode == 0x233 ? 1u : 2u);
        c.Emit(c.Base(0x5d18000000000000UL) | ((ulong)a.Register << 8) | ((ulong)b.Register << 20) | ((ulong)b.Swizzle << 28) |
               (InstructionContext.Flag(b.Absolute) << 30) | (InstructionContext.Flag(b.Negate) << 31) | ((ulong)c.Bits(76, 4) << 35) | ((ulong)c.Bits(87, 4) << 39) |
               (InstructionContext.Flag(a.Negate) << 43) | (InstructionContext.Flag(a.Absolute) << 44) | ((ulong)combine << 45) | ((ulong)a.Swizzle << 47) |
               ((ulong)c.Bits(71, 1) << 49) | ((ulong)c.Bits(80, 1) << 50));
        return true;
    }

    // HMNMX2, exact half promotion for min/max.
    private static bool MinMax(InstructionContext c)
    {
        if (c.Bits(76, 11) != 0 || c.Bits(91, 1) != 0) return c.Fail("native half min/max mode");
        HalfSource a = c.Operands.Half(0);
        HalfSource b = c.Opcode == 0x840 ? new HalfSource(255, 0, false, false) : c.Operands.Half(1);
        if (a.Swizzle == 1 || b.Swizzle == 1 || (b.Register >= 250 && b.Register != 255)) return c.Fail("native half min/max source mode");
        for (uint lane = 0; lane < 2; lane++)
        {
            c.Operands.ExtractHalf(a, lane, 250);
            if (c.Opcode == 0x840)
            {
                c.MovImm(251, (c.Insn.W1 >> (int)(lane * 16)) & 0xffff);
                c.Operands.ExtractHalf(new HalfSource(251, 2, false, false), 0, 251);
            }
            else
            {
                c.Operands.ExtractHalf(b, lane, 251);
            }

            uint target = lane == 0 ? 252u : 250u;
            c.Emit(0x5c60000000000000UL | c.Predicate | target | (250UL << 8) | (251UL << 20) | ((ulong)c.Bits(87, 3) << 39) | ((ulong)c.Bits(90, 1) << 42));
            c.Emit(0x5ca8000000000000UL | c.Predicate | target | (1UL << 8) | (2UL << 10) | ((ulong)target << 20));
        }

        c.Emit(c.Base(0x5c18000000000000UL) | (250UL << 8) | (252UL << 20) | (16UL << 39));
        return true;
    }
}
