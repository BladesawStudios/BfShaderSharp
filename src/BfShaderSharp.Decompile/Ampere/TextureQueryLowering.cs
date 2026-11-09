using System.Numerics;

namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the texture dimension query (TXQ, TXQ.B) and the texture LOD query (TMML).</summary>
/// <remarks>
/// TXQ with two or fewer outputs writes straight to the destination; otherwise the outputs come out packed in scratch
/// registers and are copied to the first and second destination, as for TEX. TMML takes its coordinates all from the
/// first source or split over two, and either form becomes Maxwell TMML with the handle in the second source when it
/// is bindless. Results are 8.8 fixed point, as on Maxwell. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class TextureQueryLowering
{
    private static readonly uint[] CoordinateCount = [1, 2, 3, 3];

    public static void Register(OpcodeTable table)
    {
        table.Add(Dimensions, 0xb6f, 0x370);
        table.Add(LodQuery, 0xb69, 0xf69, 0x36a);
    }

    private static bool Dimensions(InstructionContext c)
    {
        bool bindless = c.Opcode == 0x370;
        uint binding = c.Bits(54, 5), mask = c.Bits(72, 4), components = (uint)BitOperations.PopCount(mask);
        uint secondDest = c.Bits(64, 8), dest = (uint)c.Dest;
        bool direct = secondDest == 255;
        if (c.Bits(59, 5) != (bindless ? 1U : 0U) || mask == 0 || (direct && components > 2) || c.Bits(76, 14) != 0 || c.Bits(91, 1) != 0 ||
            (!bindless && binding >= 18) || (bindless && (c.Bits(32, 27) != 0 || c.Bits(24, 8) >= 245)) ||
            (!direct && (secondDest >= 245 || dest >= 245)) || c.Bits(40, 14) > 0x1fff || (direct && components == 2 && (dest & 1) != 0) || dest >= 254)
            return c.Fail("native texture dimension query mode");
        if (!bindless && !c.BindTextureBuffer(binding)) return c.Fail("native texture descriptor buffer conflict");
        c.Emit((bindless ? 0xdf50000000000000UL : 0xdf48000000000000UL) | c.Predicate | (direct ? dest : 250) | ((ulong)c.Bits(24, 8) << 8) | (1UL << 22) |
               ((ulong)mask << 31) | ((ulong)(bindless ? 0U : c.Bits(40, 14)) << 36) | ((ulong)c.Bits(90, 1) << 49));
        for (uint i = 0; !direct && i < components; i++)
        {
            uint target = i < 2 ? (dest == 255 ? 255U : dest + i) : secondDest + (i - 2);
            if (target != 255) c.Emit(0x5c98000000000000UL | c.Predicate | target | (15UL << 39) | ((ulong)(250 + i) << 20));
        }

        return true;
    }

    private static bool LodQuery(InstructionContext c)
    {
        bool bindless = c.Opcode != 0xb69;
        uint handle = c.Opcode == 0xf69 ? c.UniformRegister(c.Bits(40, 6)) : c.Opcode == 0x36a ? c.Bits(32, 8) : 255;
        uint mask = c.Bits(72, 4), secondDest = c.Bits(64, 8), sourceA = c.Bits(24, 8), sourceB = c.Bits(32, 8);
        uint binding = c.Bits(54, 5), dimension = c.Bits(61, 3);
        uint coordinates = dimension < 4 ? CoordinateCount[dimension] : 0;
        uint inA = sourceB == 255 || c.Opcode == 0x36a ? coordinates : (coordinates + 1) / 2;
        if (coordinates == 0 || c.Bits(76, 4) != 0 || c.Bits(80, 10) != 0 || mask == 0 || c.Bits(59, 1) != (c.Opcode == 0x36a ? 1U : 0U) ||
            sourceA + inA > 246 || (inA < coordinates && sourceB + (coordinates - inA) > 246) ||
            (c.Opcode == 0xf69 && (handle >= 246 || c.Bits(46, 13) != 0 || c.Bits(91, 1) == 0)) ||
            (c.Opcode == 0x36a && (handle >= 246 || c.Bits(40, 19) != 0 || c.Bits(91, 1) != 0)) ||
            (!bindless && (c.Bits(40, 14) > 0x1fff || binding >= 18 || !c.BindTextureBuffer(binding))))
            return c.Fail("native texture LOD query mode");
        for (uint i = 0; i < coordinates; i++)
            c.Emit(0x5c98000000000000UL | c.Predicate | (250 + i) | (15UL << 39) | ((ulong)(i < inA ? sourceA + i : sourceB + (i - inA)) << 20));
        if (bindless) c.Emit(0x5c98000000000000UL | c.Predicate | 246 | (15UL << 39) | ((ulong)handle << 20));
        c.Emit((bindless ? 0xdf60000000000000UL : 0xdf58000000000000UL) | c.Predicate | 250 | (250UL << 8) | ((ulong)(bindless ? 246U : 255U) << 20) |
               ((ulong)(dimension * 2) << 28) | ((ulong)mask << 31) | ((ulong)(bindless ? 0U : c.Bits(40, 14)) << 36) | ((ulong)c.Bits(90, 1) << 49));
        TextureSupport.CopyResults(c, mask, secondDest);
        return true;
    }
}
