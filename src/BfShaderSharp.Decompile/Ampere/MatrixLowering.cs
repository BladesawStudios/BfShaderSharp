namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the warp-wide 8x8 half-precision matrix instructions: MOVM transpose, HMMA multiply-accumulate and LDSM load.</summary>
/// <remarks>
/// Maxwell has no tensor unit, so the matrix operations are expanded into shuffles and half-precision fused
/// multiply-adds over the lanes of the warp. With the tensor option set, HMMA is instead one marker instruction that a
/// shared helper loop implements, which keeps the code from growing at every site. All temporaries live in R246-R254;
/// R240-R245 can hold live registers or mirrored uniform registers and are never borrowed. Ported from the Eden
/// emulator (GPL-3.0-or-later).
/// </remarks>
internal static class MatrixLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(Transpose, 0x23a);
        table.Add(MultiplyAccumulate, 0x23c);
        table.Add(LoadMatrix, 0x83b);
    }

    // MOVM.16.MT88: transpose an 8x8 16-bit matrix held one row pair per lane. Lane t holds row t/4, columns 2(t%4)..+1; each half of the result comes from lane 8(t%4) + t/8 (+4) at half (t/4)%2.
    private static bool Transpose(InstructionContext c)
    {
        uint source = c.Bits(24, 8);
        if (c.Insn.W1 != 0 || c.Insn.W2 != 0 || source >= 246 || c.Dest >= 246) return c.Fail("native matrix move mode");
        c.Emit(0xf0c8000000000000UL | c.Predicate | 246); // laneid
        c.Emit(0x0400000000000000UL | c.Predicate | 249 | (246UL << 8) | (3UL << 20));
        c.Emit(0x3848000000000000UL | c.Predicate | 249 | (249UL << 8) | (3UL << 20)); // 8(t%4)
        c.Emit(0x3828000000000000UL | c.Predicate | 247 | (246UL << 8) | (3UL << 20)); // t/8
        c.Emit(0x5c10000000000000UL | c.Predicate | 249 | (249UL << 8) | (247UL << 20));
        c.Emit(0xef10000000000000UL | c.Predicate | 247 | ((ulong)source << 8) | (249UL << 20) | (1UL << 29) | (31UL << 34) | (7UL << 48));
        c.Emit(0x1c00000000000000UL | c.Predicate | 249 | (249UL << 8) | (4UL << 20));
        c.Emit(0xef10000000000000UL | c.Predicate | 248 | ((ulong)source << 8) | (249UL << 20) | (1UL << 29) | (31UL << 34) | (7UL << 48));
        c.Emit(0x3828000000000000UL | c.Predicate | 249 | (246UL << 8) | (2UL << 20));
        c.Emit(0x0400000000000000UL | c.Predicate | 249 | (249UL << 8) | (1UL << 20));
        c.Emit(0x3848000000000000UL | c.Predicate | 249 | (249UL << 8) | (4UL << 20)); // 0 or 16
        c.Emit(0x5c28000000000000UL | c.Predicate | 247 | (247UL << 8) | (249UL << 20));
        c.Emit(0x0400000000000000UL | c.Predicate | 247 | (247UL << 8) | (0xffffUL << 20));
        c.Emit(0x5c28000000000000UL | c.Predicate | 248 | (248UL << 8) | (249UL << 20));
        c.Emit(0x5c18000000000000UL | c.Predicate | c.Dest | (248UL << 8) | (247UL << 20) | (16UL << 39));
        return true;
    }

    // HMMA.1688/.16816.F16, software warp expansion.
    private static bool MultiplyAccumulate(InstructionContext c)
    {
        uint a = c.Bits(24, 8), b = c.Bits(32, 8), accumulator = c.Bits(64, 8), mode = c.Insn.W2 & ~0xffU;
        uint depth = mode == 0 ? 8U : 16U, aCount = depth / 4, bCount = depth / 8;
        uint limit = c.State.UsesUniformBank ? c.State.HighUniformBase : 246U, dest = (uint)c.Dest;
        if (c.NativePredicate != 7 || (mode != 0 && mode != 0x800) || (c.Insn.W1 & ~0xffU) != 0 || (c.Insn.W3 & 0xc00001ffU) != 0 || dest % 2 != 0 ||
            dest + 2 > limit || a % aCount != 0 || a + aCount > limit || b % bCount != 0 || b + bCount > limit ||
            (accumulator != 255 && (accumulator % 2 != 0 || accumulator + 2 > limit)))
            return c.Fail("native half matrix multiply mode");
        if (c.State.Options.Tensor)
        {
            // A software collective, not an NV cooperative-matrix operation: the backend shares a bounded loop helper instead of duplicating hundreds of
            // instructions at every HMMA site. Bit 48 tags software; bit 49 selects K=8 rather than K=16.
            c.Emit(0x5d30000000000000UL | c.Predicate | dest | ((ulong)a << 8) | ((ulong)b << 20) | ((ulong)accumulator << 39) | (1UL << 48) |
                   (InstructionContext.Flag(depth == 8) << 49));
            return true;
        }

        // Four accumulators and five temporaries, all in R246..R254.
        for (uint row = 0; row < 2; row++)
            for (uint col = 0; col < 2; col++)
                c.Operands.ExtractHalf(new HalfSource(accumulator == 255 ? 255 : accumulator + row, 0, false, false), col, 246 + row * 2 + col);
        for (uint pair = 0; pair < depth / 2; pair++) ExpandPair(c, pair, a, b, depth);
        for (uint row = 0; row < 2; row++)
            c.Emit(0x5c18000000000000UL | c.Predicate | (dest + row) | ((ulong)(247 + row * 2) << 8) | ((ulong)(246 + row * 2) << 20) | (16UL << 39));
        return true;
    }

    private static void Shuffle(InstructionContext c, uint target, uint source, uint lane) =>
        c.Emit(0xef10000000000000UL | c.Predicate | target | ((ulong)source << 8) | ((ulong)lane << 20) | (1UL << 29) | (0x1fUL << 34) | (7UL << 48));

    private static void ExpandPair(InstructionContext c, uint pair, uint a, uint b, uint depth)
    {
        c.Emit(0xf0c8000000000000UL | c.Predicate | 250); // LaneId
        c.Emit(0x0400000000000000UL | c.Predicate | 251 | (250UL << 8) | (3UL << 20));
        c.Emit(0x3848000000000000UL | c.Predicate | 251 | (251UL << 8) | (3UL << 20));
        c.Emit(0x0400000000000000UL | c.Predicate | 250 | (250UL << 8) | (0x1cUL << 20));
        c.MovImm(254, pair & 3);
        foreach (uint register in new[] { 250U, 251U })
            c.Emit(0x5c10000000000000UL | c.Predicate | register | ((ulong)register << 8) | (254UL << 20));
        for (uint row = 0; row < 2; row++)
        {
            for (uint half = 0; half < 2; half++)
            {
                Shuffle(c, 252, a + (pair >= 4 ? 2U : 0U) + row, 250);
                c.Operands.ExtractHalf(new HalfSource(252, 0, false, false), half, 252);
                for (uint col = 0; col < 2; col++)
                {
                    c.MovImm(254, col * 4);
                    c.Emit(0x5c10000000000000UL | c.Predicate | 254 | (251UL << 8) | (254UL << 20));
                    Shuffle(c, 253, b + pair / 4, 254);
                    c.Operands.ExtractHalf(new HalfSource(253, 0, false, false), half, 253);
                    uint accumulator = 246 + row * 2 + col;
                    c.Emit(0x5980000000000000UL | c.Predicate | accumulator | (252UL << 8) | (253UL << 20) | ((ulong)accumulator << 39));
                }
            }
        }

        if ((pair & 3) != 3) return;
        for (uint register = 246; register < 250; register++)
        {
            c.Emit(0x5ca8000000000000UL | c.Predicate | register | (1UL << 8) | (2UL << 10) | ((ulong)register << 20));
            if (pair + 1 < depth / 2) c.Operands.ExtractHalf(new HalfSource(register, 2, false, false), 0, register);
        }
    }

    // LDSM.16.M88.{1,2,4} (ldmatrix): 8x8 16-bit matrices from shared memory. Lanes 8m..8m+7 give the row addresses of matrix m; lane t receives the 32 bits
    // at row t/4, byte (t%4)*4 of each matrix in Rd+m. Emulated with SHFL.IDX + LDS.32.
    private static bool LoadMatrix(InstructionContext c)
    {
        uint count = 1U << (int)c.Bits(72, 2);
        bool transpose = c.Bits(78, 1) != 0, uniformOffset = c.Bits(91, 1) != 0;
        uint dest = (uint)c.Dest;
        if (c.NativePredicate != 7 || c.Bits(72, 2) == 3 || (c.Insn.W2 & ~0x08004300U) != 0 || (!uniformOffset && c.Bits(32, 8) != 0) || dest % count != 0 ||
            dest + count > (c.State.UsesUniformBank ? c.State.HighUniformBase : 246U))
            return c.Fail("native matrix load mode");
        uint address = c.Bits(24, 8);
        ulong offset = c.Bits(40, 24);
        if (uniformOffset)
        {
            uint uniform = c.UniformRegister(c.Bits(32, 8));
            if (uniform == uint.MaxValue) return c.Fail("native matrix load uniform offset");
            c.Emit(0x5c10000000000000UL | c.Predicate | 254 | ((ulong)address << 8) | ((ulong)uniform << 20));
            address = 254;
        }
        else if (address != 255 && address >= dest && address < dest + count)
        {
            c.Emit(0x5c10000000000000UL | c.Predicate | 254 | ((ulong)address << 8) | (255UL << 20));
            address = 254;
        }

        if (transpose) LoadTransposed(c, count, address, offset);
        else LoadRows(c, count, address, offset);
        return true;
    }

    // .MT88: lane t receives stored rows 2(t%4) and 2(t%4)+1 at column t/4 (16 bits each).
    private static void LoadTransposed(InstructionContext c, uint count, uint address, ulong offset)
    {
        c.Emit(0xf0c8000000000000UL | c.Predicate | 246); // laneid
        c.Emit(0x0400000000000000UL | c.Predicate | 247 | (246UL << 8) | (3UL << 20));
        c.Emit(0x3848000000000000UL | c.Predicate | 247 | (247UL << 8) | (1UL << 20)); // row 2(t%4)
        c.Emit(0x3828000000000000UL | c.Predicate | 248 | (246UL << 8) | (2UL << 20));
        c.Emit(0x3848000000000000UL | c.Predicate | 248 | (248UL << 8) | (1UL << 20)); // byte 2(t/4)
        for (uint matrix = 0; matrix < count; matrix++)
        {
            for (uint half = 0; half < 2; half++)
            {
                c.Emit(0x1c00000000000000UL | c.Predicate | 249 | (247UL << 8) | ((ulong)(matrix * 8 + half) << 20));
                c.Emit(0xef10000000000000UL | c.Predicate | 250 | ((ulong)address << 8) | (249UL << 20) | (1UL << 29) | (31UL << 34) | (7UL << 48));
                c.Emit(0x5c10000000000000UL | c.Predicate | 250 | (250UL << 8) | (248UL << 20));
                c.Emit(0xef48000000000000UL | c.Predicate | (251 + half) | (250UL << 8) | (offset << 20) | (2UL << 48)); // LDS.U16
            }

            c.Emit(0x5c18000000000000UL | c.Predicate | ((uint)c.Dest + matrix) | (252UL << 8) | (251UL << 20) | (16UL << 39));
        }
    }

    private static void LoadRows(InstructionContext c, uint count, uint address, ulong offset)
    {
        c.Emit(0xf0c8000000000000UL | c.Predicate | 250); // S2R laneid
        c.Emit(0x3828000000000000UL | c.Predicate | 251 | (250UL << 8) | (2UL << 20)); // row
        c.Emit(0x0400000000000000UL | c.Predicate | 250 | (250UL << 8) | (3UL << 20));
        c.Emit(0x3848000000000000UL | c.Predicate | 250 | (250UL << 8) | (2UL << 20)); // column bytes
        for (uint matrix = 0; matrix < count; matrix++)
        {
            uint lane = 251;
            if (matrix != 0)
            {
                c.Emit(0x1c00000000000000UL | c.Predicate | 252 | (251UL << 8) | ((ulong)(matrix * 8) << 20));
                lane = 252;
            }

            c.Emit(0xef10000000000000UL | c.Predicate | 253 | ((ulong)address << 8) | ((ulong)lane << 20) | (1UL << 29) | (31UL << 34) | (7UL << 48));
            c.Emit(0x5c10000000000000UL | c.Predicate | 253 | (253UL << 8) | (250UL << 20));
            c.Emit(0xef48000000000000UL | c.Predicate | ((uint)c.Dest + matrix) | (253UL << 8) | (offset << 20) | (4UL << 48));
        }
    }
}
