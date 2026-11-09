namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the warp-level instructions: votes, shuffles, matches, reductions, swizzled adds and register-to-uniform broadcasts.</summary>
/// <remarks>
/// Votes and shuffles map onto the Maxwell instructions of the same name. R2UR broadcasts the first active lane's value
/// into a uniform register, MATCH.ALL compares every lane with the lowest active one, and REDUX is unrolled over the
/// 32 lanes, with inactive lanes contributing the identity of the operation. Ported from the Eden emulator
/// (GPL-3.0-or-later).
/// </remarks>
internal static class WarpLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(Vote, 0x806);
        table.Add(UniformVote, 0x886);
        table.Add(RegisterToUniform, 0x3c2);
        table.Add(ShuffleImmediateLane, 0xf89);
        table.Add(ShuffleImmediateLaneRegisterMask, 0x989);
        table.Add(ShuffleRegisterLane, 0x589);
        table.Add(SwizzledAdd, 0x822);
        table.Add(Match, 0x3a1);
        table.Add(Reduce, 0x3c4);
    }

    // VOTE: register ballot, ordinary predicate input and output.
    private static bool Vote(InstructionContext c)
    {
        if (c.Insn.W1 != 0 || c.Bits(72, 2) > 2 || (c.Insn.W2 & ~0x078e0300U) != 0) return c.Fail("native vote mode");
        c.Emit(c.Base(0x50d8000000000000UL) | ((ulong)c.Bits(87, 4) << 39) | ((ulong)c.Bits(81, 3) << 45) | ((ulong)c.Bits(72, 2) << 48));
        return true;
    }

    // VOTEU ballot and independent uniform predicate output.
    private static bool UniformVote(InstructionContext c)
    {
        uint output = c.Bits(81, 3);
        uint mappedTarget = c.UniformRegister(c.Bits(16, 8));
        if (mappedTarget == uint.MaxValue || c.Bits(24, 8) != 0 || c.Insn.W1 != 0 || (c.Insn.W2 & ~0x078e0300U) != 0 || c.Bits(72, 2) > 2 || c.NativePredicate != 7)
            return c.Fail("native uniform vote mode");
        uint temporary = output == 7 ? 7 : 6 - output;
        uint mask = 1U << (int)temporary;
        if (output != 7) c.Emit(0x38e8000000070000UL | 253 | (255UL << 8) | ((ulong)mask << 20));
        c.Emit(0x50d8000000070000UL | mappedTarget | ((ulong)c.Bits(87, 4) << 39) | ((ulong)temporary << 45) | ((ulong)c.Bits(72, 2) << 48));
        if (output == 7) return true;
        c.Emit(0x38e8000000070000UL | 239 | (239UL << 8) | ((ulong)mask << 20));
        c.Emit(0x38f0000000070000UL | (253UL << 8) | ((ulong)mask << 20));
        return true;
    }

    // R2UR: broadcast the first lane satisfying the execution predicate.
    private static bool RegisterToUniform(InstructionContext c)
    {
        uint target = c.UniformRegister((uint)c.Dest);
        if (target == uint.MaxValue || target == 255 || c.Insn.W1 != 0 || c.Insn.W2 != 0x000e0000) return c.Fail("native register-to-uniform mode");
        c.SavePredicate();
        c.Emit(0x50d8000000070000UL | 250 | ((ulong)c.NativePredicate << 39) | (6UL << 45) | (1UL << 48));
        c.Emit(0x5c10000000070000UL | 251 | (250UL << 8) | (255UL << 20) | (1UL << 49));
        c.Emit(0x5c40000000070000UL | 250 | (250UL << 8) | (251UL << 20) | (7UL << 48));
        c.Emit(0x5c30000000070000UL | 250 | (250UL << 20));
        c.Emit(0xef10000000070000UL | 251 | ((ulong)c.Bits(24, 8) << 8) | (250UL << 20) | (1UL << 29) | (31UL << 34) | (7UL << 48));
        if (c.NativePredicate == 7)
        {
            // Every lane executing it is in the ballot, so no lane-less fallback can be taken. Leaving the old value selectable would make it a
            // source of the result for the texture and global memory tracking (a handle traced to the uniform's last load).
            c.Emit(0x5c98000000070000UL | target | (15UL << 39) | (251UL << 20));
        }
        else
        {
            c.Emit(0x5ca0000000070000UL | target | (251UL << 8) | ((ulong)target << 20) | (6UL << 39));
        }

        c.RestorePredicate();
        return true;
    }

    // SHFL with immediate lane and segmentation/clamp mask, any mode.
    private static bool ShuffleImmediateLane(InstructionContext c)
    {
        if (c.Insn.W2 != 0x000e0000 || c.Bits(32, 8) != 0 || c.Bits(60, 4) != 0) return c.Fail("native immediate shuffle mode");
        c.Emit(c.Base(0xef10000000000000UL) | ((ulong)c.Bits(24, 8) << 8) | ((ulong)c.Bits(53, 5) << 20) | (1UL << 28) | (1UL << 29) |
               ((ulong)c.Bits(58, 2) << 30) | ((ulong)c.Bits(40, 13) << 34) | (7UL << 48));
        return true;
    }

    // SHFL, immediate lane and register clamp/segmentation mask, any mode.
    private static bool ShuffleImmediateLaneRegisterMask(InstructionContext c)
    {
        if ((c.Insn.W1 & 0xf01fffffU) != 0 || (c.Insn.W2 & ~0x000e00ffU) != 0) return c.Fail("native register-mask shuffle mode");
        c.Emit(c.Base(0xef10000000000000UL) | ((ulong)c.Bits(24, 8) << 8) | ((ulong)c.Bits(53, 5) << 20) | (1UL << 28) | ((ulong)c.Bits(58, 2) << 30) |
               ((ulong)c.Bits(64, 8) << 39) | ((ulong)c.Bits(81, 3) << 48));
        return true;
    }

    // SHFL, register lane and immediate clamp/segmentation mask, any mode.
    private static bool ShuffleRegisterLane(InstructionContext c)
    {
        uint lane = c.Bits(32, 8);
        if ((c.Insn.W2 & ~0x000e0000U) != 0 || c.Bits(53, 5) != 0 || c.Bits(60, 4) != 0 || (c.Insn.W3 & 0xc00001ffU) != 0 ||
            (lane >= (c.State.UsesUniformBank ? c.State.HighUniformBase : 246U) && lane != 255))
            return c.Fail("native shuffle mode");
        c.Emit(c.Base(0xef10000000000000UL) | ((ulong)c.Bits(24, 8) << 8) | ((ulong)lane << 20) | (1UL << 29) | ((ulong)c.Bits(58, 2) << 30) |
               ((ulong)c.Bits(40, 13) << 34) | ((ulong)c.Bits(81, 3) << 48));
        return true;
    }

    // FSWZADD: per lane 0 = a+b, 1 = a-b, 2 = b-a, 3 = b (nvdisasm PP/PN/NP/ZP).
    private static bool SwizzledAdd(InstructionContext c)
    {
        uint swizzle = c.Insn.W1;
        if (swizzle > 0xff || c.Insn.W2 > 255) return c.Fail("native swizzled add mode");
        // Maxwell's lane codes 1 and 2 are the other way round (1 = b-a, 2 = a-b).
        uint maxwell = ((swizzle & 0x55) << 1) | ((swizzle & 0xaa) >> 1);
        c.Emit(c.Base(0x50f8000000000000UL) | ((ulong)c.Bits(24, 8) << 8) | ((ulong)c.Bits(64, 8) << 20) | ((ulong)maxwell << 28));
        return true;
    }

    // MATCH.ALL (.U32, or .U64 on bit 73) with only the all-equal predicate retained.
    private static bool Match(InstructionContext c)
    {
        uint source = c.Bits(24, 8), target = c.Bits(81, 3);
        uint words = c.Bits(73, 1) != 0 ? 2u : 1u;
        uint scratch = target == 6 ? 5u : 6u; // the comparison, saved around the match
        if (c.Dest != 255 || source >= 254 || (words == 2 && (source & 1) != 0) || target == 7 || c.NativePredicate != 7 || c.Insn.W1 != 0 ||
            (c.Insn.W2 & ~0xe0200U) != 0)
            return c.Fail("native warp match mode");
        c.SavePredicate(1U << (int)scratch);
        // Compare every lane with the lowest active one, then require all to agree.
        c.Emit(0x50d8000000070000UL | 250 | (7UL << 39) | (7UL << 45) | (1UL << 48));
        c.Emit(0x5c10000000070000UL | 251 | (250UL << 8) | (255UL << 20) | (1UL << 49));
        c.Emit(0x5c40000000070000UL | 250 | (250UL << 8) | (251UL << 20) | (7UL << 48));
        c.Emit(0x5c30000000070000UL | 250 | (250UL << 20));
        for (uint word = 0; word < words; word++)
            c.Emit(0xef10000000070000UL | (251 + word) | ((ulong)(source + word) << 8) | (250UL << 20) | (1UL << 29) | (31UL << 34) | (7UL << 48));
        c.Emit(0x5b60000000070000UL | 7 | ((ulong)scratch << 3) | ((ulong)source << 8) | (251UL << 20) | (7UL << 39) | (2UL << 49));
        if (words == 2)
            c.Emit(0x5b60000000070000UL | 7 | ((ulong)scratch << 3) | ((ulong)(source + 1) << 8) | (252UL << 20) | ((ulong)scratch << 39) | (2UL << 49));
        c.Emit(0x50d8000000070000UL | 255 | ((ulong)scratch << 39) | ((ulong)target << 45));
        c.RestorePredicate(1U << (int)scratch);
        return true;
    }

    // REDUX.SUM (modulo 2^32), .MIN/.MAX (unsigned), .OR or .XOR over the actual active lanes.
    private static bool Reduce(InstructionContext c)
    {
        uint source = c.Bits(24, 8), target = c.UniformRegister((uint)c.Dest);
        uint mode = c.Insn.W2 & ~0x200U;
        bool maximum = mode == 0x14000, minimum = mode == 0x10000, signedOrder = c.Bits(73, 1) != 0;
        // OR and XOR: inactive lanes contribute zero, as for the sum. Maxwell LOP: 1 = OR, 2 = XOR.
        uint logical = c.Insn.W2 == 0x4000 ? 1u : c.Insn.W2 == 0x8000 ? 2u : 0u;
        if (target == uint.MaxValue || target == 255 || c.Insn.W1 != 0 || (c.Insn.W2 != 0xc000 && !maximum && !minimum && logical == 0) ||
            (signedOrder && !maximum && !minimum) || c.NativePredicate != 7)
            return c.Fail("native warp sum mode");
        c.Emit(0x50d8000000070000UL | 250 | (7UL << 39) | (7UL << 45) | (1UL << 48));
        c.MovImm(target, minimum ? uint.MaxValue : 0U);
        for (uint lane = 0; lane < 32; lane++) ReduceLane(c, lane, source, target, (maximum, minimum, signedOrder, logical));
        if (signedOrder) c.Emit(0x0400000000070000UL | target | ((ulong)target << 8) | (0x80000000UL << 20) | (2UL << 53));
        return true;
    }

    private static void ReduceLane(InstructionContext c, uint lane, uint source, uint target, (bool Maximum, bool Minimum, bool Signed, uint Logical) kind)
    {
        c.Emit(0xef10000000070000UL | 251 | ((ulong)source << 8) | ((ulong)lane << 20) | (1UL << 28) | (1UL << 29) | (31UL << 34) | (7UL << 48));
        if (kind.Signed) c.Emit(0x0400000000070000UL | 251 | (251UL << 8) | (0x80000000UL << 20) | (2UL << 53));
        c.Emit(0x3828000000070000UL | 252 | (250UL << 8) | ((ulong)lane << 20));
        c.Emit(0x0400000000070000UL | 252 | (252UL << 8) | (1UL << 20));
        c.Emit(0x5c10000000070000UL | 252 | (255UL << 8) | (252UL << 20) | (1UL << 48));
        // Inactive lanes: 0, or ~0 for the minimum (OR with the inverted lane mask).
        c.Emit(0x5c40000000070000UL | 251 | (251UL << 8) | (252UL << 20) | (7UL << 48) | (InstructionContext.Flag(kind.Minimum) << 40) | (InstructionContext.Flag(kind.Minimum) << 41));
        if (kind.Minimum) c.Emit(0x5c20000000070000UL | target | ((ulong)target << 8) | (251UL << 20) | (7UL << 39));
        else if (kind.Maximum) c.Emit(0x5c20000000070000UL | target | ((ulong)target << 8) | (251UL << 20) | (15UL << 39));
        else if (kind.Logical != 0) c.Emit(0x5c40000000070000UL | target | ((ulong)target << 8) | (251UL << 20) | ((ulong)kind.Logical << 41) | (7UL << 48));
        else c.Emit(0x5c10000000070000UL | target | ((ulong)target << 8) | (251UL << 20));
    }
}
