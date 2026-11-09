namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the uniform-datapath moves, constant loads and bit operations onto the ordinary registers that mirror the uniform ones.</summary>
/// <remarks>
/// A uniform register is an ordinary register chosen by <see cref="InstructionContext.UniformRegister"/>, so UMOV, ULDC,
/// UFLO, UBREV, UPOPC and USHF become the per-thread instruction on the mirrored registers. Register 63 (URZ) maps to
/// RZ and discards writes. The shift forms accepted are the ones the shaders use: a left shift, a logical right shift of
/// one register, and a funnel shift of two. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class UniformDatapathLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(MoveImmediate, 0x882);
        table.Add(MoveRegister, 0xc82);
        table.Add(LoadConstant, 0xab9, 0xabb);
        table.Add(Shift, 0x899);
        table.Add(LeadingBit, 0x2bd);
        table.Add(BitReverse, 0x2be);
        table.Add(PopulationCount, 0x2bf);
        table.Add(IndexedConstantLoad, 0xb82);
    }

    // UMOV immediate, into any uniform register.
    private static bool MoveImmediate(InstructionContext c)
    {
        uint target = c.UniformRegister((uint)c.Dest);
        if (target == uint.MaxValue || c.Insn.W2 != 0) return c.Fail("native uniform immediate move mode");
        if (target != 255) c.MovImm(target, c.Insn.W1);
        return true;
    }

    // UMOV UR, UR (or URZ), including uniform execution predicates, into any uniform destination.
    private static bool MoveRegister(InstructionContext c)
    {
        uint source = c.Insn.W1 < 64 ? c.UniformRegister(c.Insn.W1) : uint.MaxValue;
        uint target = c.UniformRegister((uint)c.Dest);
        if (target == uint.MaxValue || source == uint.MaxValue || c.Insn.W2 != 0x08000000) return c.Fail("native uniform move mode");
        if (target != 255) c.Emit(0x5c98000000000000UL | c.Predicate | target | ((ulong)source << 20) | (15UL << 39));
        return true;
    }

    // ULDC, direct or indexed.
    private static bool LoadConstant(InstructionContext c)
    {
        uint opcode = c.Opcode, uniformDestination = c.Bits(16, 8), indexRegister = c.Bits(24, 8), width = c.Bits(73, 3);
        bool subwordLoad = opcode == 0xab9 && (width == 0 || width == 2);
        uint offset = c.Bits(38, 16);
        uint alignment = width == 0 ? 1u : width == 2 ? 2u : 4u;
        uint target = c.UniformRegister(uniformDestination);
        uint indexTarget = opcode == 0xabb ? c.UniformRegister(indexRegister) : 255U;
        if (target == uint.MaxValue || target == 255 || (!subwordLoad && width != 4 && width != 5) || (subwordLoad && offset >= 0x8000) ||
            (width == 5 && ((uniformDestination & 1) != 0 || c.UniformRegister(uniformDestination + 1) != target + 1)) ||
            c.Bank() >= 18 || offset % alignment != 0 || c.Bits(62, 2) != 0 || (c.Insn.W2 & ~0xe00U) != (opcode == 0xab9 ? 0U : 0x08000000U) ||
            indexTarget == uint.MaxValue)
            return c.Fail("native uniform constant load mode");
        if (opcode == 0xab9 && width == 4)
            c.Emit(0x4c98000000000000UL | c.Predicate | target | (15UL << 39) | c.Cbuf());
        else
            c.Emit(0xef90000000000000UL | c.Predicate | target | ((ulong)indexTarget << 8) | ((ulong)offset << 20) | ((ulong)c.Bank() << 36) | ((ulong)width << 48));
        return true;
    }

    // USHF.L.U32; accepts a zero word outside the selected half.
    private static bool Shift(InstructionContext c)
    {
        uint destination = c.Bits(16, 8), low = c.Bits(24, 8), high = c.Bits(64, 8);
        uint destinationRegister = c.UniformRegister(destination), lowRegister = c.UniformRegister(low), highRegister = c.UniformRegister(high);
        bool destinationOk = destination != 63 && destinationRegister != uint.MaxValue;
        bool lowOk = low != 63 && lowRegister != uint.MaxValue, highOk = high != 63 && highRegister != uint.MaxValue;
        uint amount = c.Insn.W1, mode = c.Insn.W2;
        if (destinationOk && lowRegister != uint.MaxValue && highRegister != uint.MaxValue && mode == (0x08001200U | high))
        {
            c.MovImm(252, amount);
            c.Integer.RightShiftU64Low(destinationRegister, lowRegister, highRegister, 252);
            return true;
        }

        // A UPn guard (@UP1 USHF.R.U32.HI) is swapped into the predicate by the prologue.
        if (mode == (0x08011400U | high) && low == 63 && highOk && destinationOk && amount <= 32)
        {
            c.Emit(0x3828000000000000UL | c.Predicate | destinationRegister | ((ulong)highRegister << 8) | ((ulong)amount << 20) | (1UL << 48));
            return true;
        }

        // Logical right shifts of one register (bit 76 right, bit 80 high word, bits 73-74 U32): USHF.R.U32 URd, URa, n, URZ and USHF.R.U32.HI URd, URZ, n, URb.
        bool rightLow = mode == 0x0800163f && lowOk;
        bool rightHigh = mode == (0x08011600U | high) && low == 63 && highOk;
        if ((rightLow || rightHigh) && destinationOk && amount <= 32)
        {
            c.Emit(0x3828000000000000UL | c.Predicate | destinationRegister | ((ulong)(rightHigh ? highRegister : lowRegister) << 8) | ((ulong)amount << 20));
            return true;
        }

        bool highResult = mode == (0x08010600U | high) && low == 63 && highOk;
        bool lowResult = mode == 0x0800063f && lowOk;
        if (!destinationOk || amount > 32 || (!highResult && !lowResult)) return c.Fail("native uniform shift mode");
        c.Emit(0x3848000000000000UL | c.Predicate | destinationRegister | ((ulong)(highResult ? highRegister : lowRegister) << 8) | ((ulong)amount << 20));
        return true;
    }

    // UFLO.U32, the highest set-bit index (.SH, bit 74: its shift amount).
    private static bool LeadingBit(InstructionContext c)
    {
        uint target = c.UniformRegister((uint)c.Dest), source = c.UniformRegister(c.Bits(32, 8));
        if (c.Dest == 63 || target == uint.MaxValue || source == uint.MaxValue || (c.Insn.W1 & ~0xffU) != 0 || (c.Insn.W2 & ~0x400U) != 0x080e0000 || c.NativePredicate != 7)
            return c.Fail("native uniform leading bit mode");
        c.Emit(0x5c30000000000000UL | c.Predicate | target | ((ulong)source << 20) | ((ulong)c.Bits(74, 1) << 41));
        return true;
    }

    // UBREV, as a bit-reversing zero shift like BREV.
    private static bool BitReverse(InstructionContext c)
    {
        uint target = c.UniformRegister((uint)c.Dest), source = c.UniformRegister(c.Bits(32, 8));
        if (c.Dest == 63 || target == uint.MaxValue || source == uint.MaxValue || (c.Insn.W1 & ~0xffU) != 0 || c.Insn.W2 != 0x08000000 || c.NativePredicate != 7)
            return c.Fail("native uniform bit reverse mode");
        c.Emit(0x3828000000000000UL | c.Predicate | target | ((ulong)source << 8) | (1UL << 39) | (1UL << 40));
        return true;
    }

    // UPOPC, as POPC of the uniform's register.
    private static bool PopulationCount(InstructionContext c)
    {
        uint target = c.UniformRegister((uint)c.Dest), source = c.UniformRegister(c.Bits(32, 8));
        if (c.Dest == 63 || target == uint.MaxValue || source == uint.MaxValue || (c.Insn.W1 & ~0x800000ffU) != 0 || c.Insn.W2 != 0x08000000 || c.NativePredicate != 7)
            return c.Fail("native uniform population count mode");
        c.Emit(0x5c08000000000000UL | c.Predicate | target | ((ulong)source << 20) | ((ulong)c.Bits(63, 1) << 40));
        return true;
    }

    // LDC indexed byte offset; bits 78-79 the mode as Maxwell's (2 = IS), which addresses all constant buffers as one space with the buffer in its high half.
    private static bool IndexedConstantLoad(InstructionContext c)
    {
        uint width = c.Bits(73, 3), mode = c.Bits(78, 2);
        if (width > 5 || (mode != 0 && mode != 2) || (c.Insn.W2 & ~0xce00U) != 0 || (c.Insn.W1 & 0xf800003fU) != 0 || c.Bank() >= 18 ||
            (width == 5 && ((c.Dest & 1) != 0 || c.Dest >= 254)))
            return c.Fail("native indexed constant load mode");
        c.Emit(c.Base(0xef90000000000000UL) | ((ulong)c.Bits(24, 8) << 8) | ((ulong)c.Bits(38, 16) << 20) | ((ulong)c.Bank() << 36) | ((ulong)mode << 44) | ((ulong)width << 48));
        return true;
    }
}
