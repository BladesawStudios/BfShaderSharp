namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the instructions that compute with predicates: PLOP3, UPLOP3, UP2UR and CSMTEST.</summary>
/// <remarks>
/// A predicate lookup table of up to three inputs becomes a Maxwell predicate instruction when it is a plain AND or OR,
/// and the general lookup of <see cref="PredicateLogic"/> otherwise. Uniform predicates live in bits of R239, so the
/// uniform forms load them into P0-P6 around the operation and merge the result back. Ported from the Eden emulator
/// (GPL-3.0-or-later).
/// </remarks>
internal static class PredicateLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(PredicateLut, 0x81c);
        table.Add(UniformPredicateLut, 0x89c);
        table.Add(UniformPredicatesToRegister, 0x883);
        table.Add(ShadingModeTest, 0x80d);
    }

    // PLOP3.LUT; bit 67 makes the third source a uniform predicate.
    private static bool PredicateLut(InstructionContext c)
    {
        uint target = c.Bits(81, 3), secondTarget = c.Bits(84, 3);
        uint lut = c.Bits(64, 3) | (c.Bits(72, 5) << 3);
        bool uniformThird = c.Bits(67, 1) != 0;
        if (secondTarget != 7)
        {
            if (target == secondTarget || c.Bits(24, 8) != 0 || c.Insn.W1 != 0 || c.Bits(91, 5) != 0 || (c.Insn.W3 & 0xc00001ffU) != 0)
                return c.Fail("native dual predicate LUT mode");
            c.Predicates.Lut(lut, c.Bits(87, 4), c.Bits(77, 4), c.Bits(68, 4), target, uniformThird, secondTarget, c.Bits(16, 8));
            return true;
        }

        if (target == 7 || c.Bits(84, 3) != 7 || c.Bits(16, 8) != 0 || c.Insn.W1 != 0) return c.Fail("native predicate LUT mode");
        if (!uniformThird && (lut == 0xfe || lut == 0xa8))
        {
            c.Emit(0x5090000000000000UL | c.Predicate | ((ulong)target << 3) | 7 | ((ulong)c.Bits(87, 4) << 12) | (1UL << 24) | ((ulong)c.Bits(77, 4) << 29) |
                   ((ulong)c.Bits(68, 4) << 39) | (InstructionContext.Flag(lut == 0xfe) << 45));
            return true;
        }

        if (!uniformThird && (lut == 0x80 || (lut == 8 && c.Bits(68, 4) == 7 && c.Bits(77, 4) == 7)))
        {
            c.Emit(0x5090000000000000UL | c.Predicate | ((ulong)target << 3) | 7 | ((ulong)(c.Bits(87, 4) ^ (lut == 8 ? 8U : 0U)) << 12) |
                   ((ulong)c.Bits(77, 4) << 29) | ((ulong)c.Bits(68, 4) << 39));
            return true;
        }

        c.Predicates.Lut(lut, c.Bits(87, 4), c.Bits(77, 4), c.Bits(68, 4), target, uniformThird);
        return true;
    }

    // UPLOP3.LUT: load the uniform predicates into P0-P6, combine, merge back.
    private static bool UniformPredicateLut(InstructionContext c)
    {
        uint target = c.Bits(81, 3);
        uint lut = c.Bits(64, 3) | (c.Bits(72, 5) << 3);
        if (c.Bits(84, 3) != 7 || c.Bits(67, 1) != 0 || c.Bits(16, 8) != 0 || c.Insn.W1 != 0) return c.Fail("native uniform predicate LUT mode");
        if (target == 7) return true;
        c.Predicate = (ulong)Slot(c.NativePredicate) << 16; // a UPn guard is loaded as well
        c.Emit(0x38e8000000070000UL | 254 | (255UL << 8) | (0x7fUL << 20));
        c.Emit(0x38f0000000070000UL | (239UL << 8) | (0x7fUL << 20));
        c.Predicates.Lut(lut, Slot(c.Bits(87, 4)), Slot(c.Bits(77, 4)), Slot(c.Bits(68, 4)), 6 - target);
        c.Emit(0x38e8000000070000UL | 239 | (239UL << 8) | ((ulong)(1U << (int)(6 - target)) << 20));
        c.Emit(0x38f0000000070000UL | (254UL << 8) | (0x7fUL << 20));
        return true;
    }

    private static uint Slot(uint operand) => (operand & 7) == 7 ? operand : (operand & 8) | (6 - (operand & 7));

    // UP2UR UR, UPR, URZ, mask: bit k of the result is UPk (R239 bit 6-k).
    private static bool UniformPredicatesToRegister(InstructionContext c)
    {
        uint target = c.UniformRegister(c.Bits(16, 8)), mask = c.Insn.W1;
        if (target == uint.MaxValue || c.Bits(24, 8) != 63 || c.NativePredicate != 7 || c.Insn.W2 != 0x08000000 || mask > 0x7f)
            return c.Fail("native uniform predicate move mode");
        if (target == 255) return true;
        c.MovImm(target, 0);
        for (uint k = 0; k < 7; k++)
        {
            if (((mask >> (int)k) & 1) == 0) continue;
            c.Emit(0x3800000000070000UL | 250 | (239UL << 8) | ((ulong)((6 - k) | (1U << 8)) << 20));
            c.Emit(0x36f0000000070000UL | target | (250UL << 8) | ((ulong)(k | (1U << 8)) << 20) | ((ulong)target << 39));
        }

        return true;
    }

    // CSMTEST: a test of shading-mode state the translator does not model, taken as false. Outputs of PT discard the result.
    private static bool ShadingModeTest(InstructionContext c)
    {
        if (c.Bits(87, 4) != 7 || c.Bits(16, 16) != 0) return c.Fail("native CSM test mode");
        foreach (uint target in new[] { c.Bits(81, 3), c.Bits(84, 3) })
        {
            if (target == 7) continue;
            c.Emit(0x5090000000000000UL | c.Predicate | ((ulong)target << 3) | 7 | (7UL << 12) | (1UL << 15) | (7UL << 29) | (7UL << 39));
        }

        return true;
    }
}
