namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers shared-memory loads and stores with scaled offsets, memory and workgroup barriers, and the asynchronous global-to-shared copy.</summary>
/// <remarks>
/// A shared access with a scaled register index or a uniform base first adds them into a scratch register. Barriers map
/// onto MEMBAR and BAR.SYNC with the scope translated to Maxwell's. The asynchronous copy completes synchronously: a
/// disabled copy zero-fills its destination without touching global memory. Ported from the Eden emulator
/// (GPL-3.0-or-later).
/// </remarks>
internal static class SharedMemoryLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(Nop, 0x918);
        table.Add(DependencyBarrier, 0x91a);
        table.Add(CommitCopyGroup, 0x9af);
        table.Add(MemoryBarrier, 0x992);
        table.Add(ErrorBarrier, 0x9ab);
        table.Add(WorkgroupBarrier, 0xb1d);
        table.Add(AsyncCopy, 0xfae);
        table.Add(Access, 0x388, 0x984, 0x988);
    }

    private static bool Nop(InstructionContext c)
    {
        if (c.Insn.W1 != 0 || c.Insn.W2 != 0) return c.Fail("native NOP mode");
        c.Emit(0x50b0000000000f00UL | c.Predicate);
        return true;
    }

    // MEMBAR.SC: preserve workgroup/device visibility.
    private static bool MemoryBarrier(InstructionContext c)
    {
        uint scope = c.Bits(76, 3);
        if (c.Insn.W1 != 0 || c.Insn.W2 != (scope << 12) || (scope != 0 && scope != 2 && scope != 3 && scope != 5)) return c.Fail("native memory barrier scope");
        uint legacyScope = scope == 0 ? 0 : scope == 2 ? 1u : scope == 3 ? 2u : 3u;
        c.Emit(0xef98000000000000UL | c.Predicate | ((ulong)legacyScope << 8));
        return true;
    }

    // ERRBAR: wait for prior memory operations on the host backend.
    private static bool ErrorBarrier(InstructionContext c)
    {
        if (c.Insn.W1 != 0 || c.Insn.W2 != 0) return c.Fail("native error barrier mode");
        c.Emit(0xef98000000000000UL | c.Predicate | (1UL << 8));
        return true;
    }

    // BAR.SYNC 0 (bit 80: .DEFER_BLOCKING), full workgroup.
    private static bool WorkgroupBarrier(InstructionContext c)
    {
        if (c.Insn.W1 != 0 || (c.Insn.W2 & ~0x10000U) != 0 || c.NativePredicate != 7) return c.Fail("native workgroup barrier mode");
        c.Emit(0xf0a8000000000000UL | c.Predicate | (1UL << 43) | (1UL << 44) | (1UL << 39) | (7UL << 39));
        return true;
    }

    // LDGSTS.E.BYPASS.128 [shared], [global.64], enable. Copies complete synchronously; disabled sources zero-fill without touching global memory.
    private static bool AsyncCopy(InstructionContext c)
    {
        uint shared = (uint)c.Dest, global = c.Bits(24, 8), limit = c.State.UsesUniformBank ? c.State.HighUniformBase : 246U;
        bool promotedCopy = (c.Insn.W2 & ~0x0780003fU) == 0x08101d40U;
        uint descriptor = c.Bits(64, 6);
        bool descriptorCopy = (c.Insn.W2 & ~0x0380007fU) == 0x0c101c00U && descriptor < 63 && (descriptor & 1) == 0;
        if ((c.Insn.W1 & 0x0000f000U) != 0 || (!promotedCopy && !descriptorCopy) || shared >= limit || (global & 1) != 0 || global + 1 >= limit)
            return c.Fail("native global-to-shared copy mode");
        int globalOffset = (int)(c.Bits(32, 12) << 20) >> 20;
        int sharedOffset = (int)(c.Bits(48, 16) << 16) >> 12;
        for (uint component = 0; component < 4; component++) c.MovImm(248 + component, 0);
        uint enable = c.Bits(87, 3) | (c.Bits(promotedCopy ? 90 : 70, 1) << 3);
        uint skip = (uint)c.Output.Count;
        c.Emit(0);
        c.Emit(0xeed0000000000000UL | c.Predicate | 248 | ((ulong)global << 8) | ((ulong)((uint)globalOffset & 0xffffff) << 20) | (1UL << 45) | (6UL << 48));
        uint target = (uint)c.Output.Count;
        uint distance = LoweringState.AddressOf(target) - LoweringState.AddressOf(skip) - 8;
        c.Output[(int)skip] = 0xe24000000000000fUL | ((ulong)(enable ^ 8U) << 16) | ((ulong)distance << 20);
        c.Emit(0xef58000000000000UL | c.Predicate | 248 | ((ulong)shared << 8) | ((ulong)((uint)sharedOffset & 0xffffff) << 20) | (6UL << 48));
        return true;
    }

    // Shared memory load/store with scaled register offsets. LDS [Ra.Xn + URb + offset]: bit 91 adds a uniform register (bits 32-39); STS with one has it in bits 64-71, the data at 32.
    private static bool Access(InstructionContext c)
    {
        uint opcode = c.Opcode, width = c.Bits(73, 3), stride = c.Bits(78, 2);
        bool uniformBase = (opcode == 0x984 || opcode == 0x988) && c.Bits(91, 1) != 0;
        uint baseRegister = uniformBase ? c.UniformRegister(c.Bits(opcode == 0x988 ? 64 : 32, 8)) : 255U;
        uint ignored = uniformBase ? (opcode == 0x988 ? 0x080000ffU : 0x08000000U) : 0U;
        if (width > 6 || (c.Insn.W2 & ~ignored) != ((width << 9) | (stride << 14)) || (opcode == 0x984 && !uniformBase && c.Bits(32, 8) != 0) ||
            (opcode == 0x988 && !uniformBase) || baseRegister == uint.MaxValue)
            return c.Fail("native shared memory addressing mode");
        uint address = c.Bits(24, 8);
        if (stride != 0 && address != 255)
        {
            c.Emit(0x3848000000000000UL | c.Predicate | 250 | ((ulong)address << 8) | ((ulong)(stride + 1) << 20));
            address = 250;
        }

        if (baseRegister != 255)
        {
            c.Emit(0x5c10000000000000UL | c.Predicate | 250 | ((ulong)address << 8) | ((ulong)baseRegister << 20));
            address = 250;
        }

        bool store = opcode != 0x984;
        uint data = store ? c.Bits(32, 8) : (uint)c.Dest;
        c.Emit((store ? 0xef58000000000000UL : 0xef48000000000000UL) | c.Predicate | data | ((ulong)address << 8) | ((ulong)c.Bits(40, 24) << 20) | ((ulong)width << 48));
        return true;
    }

    // DEPBAR.LE: scoreboard waits are represented by IR data dependencies; scoreboard 0x8000 waits on copy groups, whose copies are synchronous, so only their stores become visible.
    private static bool DependencyBarrier(InstructionContext c)
    {
        uint scoreboard = c.Insn.W1 & ~0xfffU;
        if (c.Insn.W0 != 0x791a || (c.Insn.W1 & ~0x7fffU) != 0x8000 || c.Insn.W2 != 0) return c.Fail("native dependency barrier mode");
        if (c.State.UsesAsyncCopy && scoreboard == 0x8000) c.Emit(0xef98000000000000UL | c.Predicate);
        return true;
    }

    // LDGDEPBAR: commit a copy group (the copies are already complete).
    private static bool CommitCopyGroup(InstructionContext c)
    {
        if (c.Insn.W0 != 0x79af || c.Insn.W1 != 0 || c.Insn.W2 != 0) return c.Fail("native copy-group commit mode");
        c.Emit(0xef98000000000000UL | c.Predicate);
        return true;
    }
}
