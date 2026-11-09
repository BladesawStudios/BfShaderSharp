using System.Numerics;

namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers direct texel fetches (TLD) by descriptor buffer, by register handle and by uniform handle.</summary>
/// <remarks>
/// A fetch from the program's descriptor buffer becomes Maxwell TLDS (2D, 3D and array forms, with an optional level
/// or texel offset) or, for an array with a level, a TLD whose packed results are copied out. A fetch from a second
/// descriptor buffer, or through a handle held in a register or a uniform register, becomes TLD.B with the
/// coordinates gathered in scratch registers. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class TexelFetchLowering
{
    private static readonly uint[] CoordinateCount = [1, 2, 3, 0, 0, 3, 0, 0];
    private static readonly uint[] BindlessType = [0, 2, 4, 0, 0, 3, 0, 0];
    private static readonly uint[] RgMasks = [1, 2, 4, 8, 3, 9, 10, 12];
    private static readonly uint[] RgbaMasks = [7, 11, 13, 14, 15];

    public static void Register(OpcodeTable table)
    {
        table.Add(Fetch, 0xb66);
        table.Add(RegisterHandle, 0x367);
        table.Add(UniformHandle, 0xf66);
    }

    // The decoded operands of the descriptor-buffer form.
    private sealed class Texel
    {
        public uint Mask, Dimension, SourceA, SourceB, SecondDest, Dest, Swizzle;
        public bool Level, Offsets, Half, Array, Volume, Split;
    }

    private static Texel Decode(InstructionContext c)
    {
        var t = new Texel
        {
            Mask = c.Bits(72, 4), Level = c.Bits(87, 3) == 3, Offsets = c.Bits(76, 1) != 0, Half = c.Bits(79, 1) != 0,
            Dimension = c.Bits(61, 3), SourceA = c.Bits(24, 8), SourceB = c.Bits(32, 8), SecondDest = c.Bits(64, 8), Dest = (uint)c.Dest,
        };
        t.Array = t.Dimension == 5;
        t.Volume = t.Dimension == 2;
        // TLDS has no single-register G+B or R+B swizzle: take alpha too, as the three-channel form, with it going to scratch R246.
        if (t.SecondDest == 255 && (t.Mask == 5 || t.Mask == 6))
        {
            t.Mask |= 8;
            t.SecondDest = 246;
        }

        t.Split = t.SecondDest != 255;
        t.Swizzle = uint.MaxValue;
        uint[] masks = t.Split ? RgbaMasks : RgMasks;
        for (uint i = 0; i < masks.Length; i++)
            if (masks[i] == t.Mask) t.Swizzle = i;
        return t;
    }

    private static bool Invalid(InstructionContext c, Texel t)
    {
        bool badOffsets = t.Offsets && (t.Dimension != 1 || (t.SourceA & 1) != 0 || t.SourceA >= 254 || t.SourceB == 255 || (t.Level && ((t.SourceB & 1) != 0 || t.SourceB >= 254)));
        bool badLevel = (t.Level && t.Volume) || (t.Level && t.Dimension == 1 && ((t.SourceA & 1) != 0 || t.SourceA >= 254)) || (t.Level && t.Array && ((t.SourceB & 1) != 0 || t.SourceB >= 254));
        bool badDest = t.Dest == 255 || (!t.Half && (t.Mask & (t.Mask - 1)) != 0 && ((t.Dest & 1) != 0 || t.Dest >= 254)) ||
            (!t.Half && t.Split && t.Mask == 15 && ((t.SecondDest & 1) != 0 || t.SecondDest >= 254));
        bool badSource = (t.Array && ((t.SourceA & 1) != 0 || t.SourceA >= 249)) || (t.Volume && ((t.SourceA & 1) != 0 || t.SourceA >= 254));
        return c.Bits(60, 1) == 0 || (t.Dimension != 0 && t.Dimension != 1 && !t.Array && !t.Volume) || t.Swizzle == uint.MaxValue ||
               (c.Bits(76, 5) & ~0x9U) != 0 || (t.Half && t.Array && t.Level) || badOffsets || c.Bits(81, 3) != 7 ||
               (c.Bits(87, 3) != 1 && !t.Level) || badLevel || c.Bits(59, 1) != 0 || c.Bits(40, 14) > 0x1fff || c.Bits(54, 5) >= 18 || badDest || badSource;
    }

    private static bool Fetch(InstructionContext c)
    {
        Texel t = Decode(c);
        if (Invalid(c, t)) return c.Fail("native texel fetch mode or descriptor binding");
        uint binding = c.Bits(54, 5);
        if (!c.BindTextureBuffer(binding))
        {
            if (t.Level || t.Offsets || t.Half) return c.Fail("native texture descriptor buffer conflict");
            SecondBuffer(c, t, binding);
            return true;
        }

        if (t.Array && t.Level)
        {
            ArrayWithLevel(c, t);
            return true;
        }

        uint sourceB = t.SourceB;
        if (t.Array)
        {
            TextureSupport.Copy(c, 250, t.SourceA + 1);
            TextureSupport.Copy(c, 251, sourceB);
            sourceB = 250;
        }

        uint mode = t.Offsets ? (t.Level ? 12U : 4U) : t.Array ? 8U : t.Volume ? 7U : t.Dimension == 0 ? (t.Level ? 1U : 0U) : (t.Level ? 5U : 2U);
        c.Emit((0xda00000000000000UL & ~((ulong)(t.Half ? 1 : 0) << 59)) | c.Predicate | t.Dest | ((ulong)t.SourceA << 8) | ((ulong)sourceB << 20) |
               ((ulong)t.SecondDest << 28) | ((ulong)c.Bits(40, 14) << 36) | ((ulong)c.Bits(90, 1) << 49) | ((ulong)t.Swizzle << 50) | ((ulong)mode << 53));
        return true;
    }

    // A second descriptor buffer: TLD.B (LZ) with the coordinates in R250.. and the handle, loaded from that buffer, in R253.
    private static void SecondBuffer(InstructionContext c, Texel t, uint binding)
    {
        TextureSupport.Copy(c, 250, t.SourceA);
        if (t.Array || t.Volume) TextureSupport.Copy(c, 251, t.SourceA + 1);
        if (t.Dimension != 0) TextureSupport.Copy(c, t.Array || t.Volume ? 252U : 251U, t.SourceB);
        c.Emit(0x4c98000000070000UL | 253 | (15UL << 39) | ((ulong)c.Bits(40, 14) << 20) | ((ulong)binding << 34));
        c.Emit(0xdd00000000000000UL | c.Predicate | 250 | (250UL << 8) | (253UL << 20) | ((ulong)(t.Array ? 3U : t.Volume ? 4U : t.Dimension == 0 ? 0U : 2U) << 28) |
               ((ulong)t.Mask << 31) | ((ulong)c.Bits(90, 1) << 49) | (7UL << 51));
        TextureSupport.CopyResults(c, t.Mask, t.SecondDest);
    }

    // TLDS has no array form with a level: TLD takes (layer, x, y) from Ra and the level from Rb; results come out packed.
    private static void ArrayWithLevel(InstructionContext c, Texel t)
    {
        for (uint i = 0; i < 2; i++)
        {
            TextureSupport.Copy(c, 250 + i, t.SourceA + i);
            TextureSupport.Copy(c, 252 + i, t.SourceB + i);
        }

        c.Emit(0xdc00000000000000UL | c.Predicate | 246 | (250UL << 8) | (253UL << 20) | (3UL << 28) | ((ulong)t.Mask << 31) | ((ulong)c.Bits(40, 14) << 36) |
               ((ulong)c.Bits(90, 1) << 49) | (7UL << 51) | (1UL << 55));
        uint components = (uint)BitOperations.PopCount(t.Mask);
        for (uint i = 0; i < components; i++)
        {
            uint target = !t.Split || i < 2 ? t.Dest + i : t.SecondDest + (i - 2);
            if (target != 255 && (t.Split || i < 2)) TextureSupport.Copy(c, target, 246 + i);
        }
    }

    // TLD.B at level zero; the values are the coordinates then the handle, split over Ra and Rb (ARRAY_2D: layer, x | y, handle).
    private static bool RegisterHandle(InstructionContext c)
    {
        uint mask = c.Bits(72, 4), secondDest = c.Bits(64, 8), sourceA = c.Bits(24, 8), sourceB = c.Bits(32, 8);
        uint dimension = c.Bits(61, 3), coordinates = CoordinateCount[dimension];
        uint count = coordinates + 1, inA = (count + 1) / 2;
        if (coordinates == 0 || c.Bits(59, 1) == 0 || c.Bits(60, 1) == 0 || c.Bits(40, 19) != 0 || c.Bits(76, 5) != 0 || c.Bits(81, 3) != 7 || c.Bits(87, 3) != 1 ||
            c.Bits(91, 1) != 0 || mask == 0 || sourceA == 255 || sourceA + inA > 246 || sourceB == 255 || sourceB + (count - inA) > 246)
            return c.Fail("native bindless texel fetch mode");
        uint Value(uint i) => i < inA ? sourceA + i : sourceB + (i - inA);
        for (uint i = 0; i < coordinates; i++) TextureSupport.Copy(c, 250 + i, Value(i));
        TextureSupport.Copy(c, 246, Value(coordinates));
        c.Emit(0xdd00000000000000UL | c.Predicate | 250 | (250UL << 8) | (246UL << 20) | ((ulong)BindlessType[dimension] << 28) | ((ulong)mask << 31) |
               ((ulong)c.Bits(90, 1) << 49) | (7UL << 51));
        TextureSupport.CopyResults(c, mask, secondDest);
        return true;
    }

    // TLD.SCR.LZ with the handle in a uniform register: TLD.B with the coordinates in R250.. and the handle in Rb.
    private static bool UniformHandle(InstructionContext c)
    {
        uint handle = c.UniformRegister(c.Bits(40, 6));
        uint mask = c.Bits(72, 4), secondDest = c.Bits(64, 8), sourceA = c.Bits(24, 8), sourceB = c.Bits(32, 8), dimension = c.Bits(61, 3), dest = (uint)c.Dest;
        bool array = dimension == 5, volume = dimension == 2, split = secondDest != 255;
        uint components = (uint)BitOperations.PopCount(mask), limit = c.State.HighUniformBase;
        if (handle == uint.MaxValue || handle >= 246 || c.Bits(46, 13) != 0 || c.Bits(91, 1) == 0 || c.Bits(60, 1) == 0 || c.Bits(59, 1) != 0 ||
            c.Bits(76, 5) != 0 || c.Bits(81, 3) != 7 || c.Bits(87, 3) != 1 || (dimension != 0 && dimension != 1 && !array && !volume) || mask == 0 ||
            ((array || volume) && sourceA + 1 >= limit) || (dest != 255 && dest + (split ? Math.Min(components, 2U) : components) > limit) ||
            (split && components > 2 && secondDest + components - 2 > limit))
            return c.Fail("native bindless texel fetch mode");
        TextureSupport.Copy(c, 250, sourceA);
        if (array || volume) TextureSupport.Copy(c, 251, sourceA + 1);
        if (dimension != 0) TextureSupport.Copy(c, array || volume ? 252U : 251U, sourceB);
        TextureSupport.Copy(c, 246, handle);
        c.Emit(0xdd00000000000000UL | c.Predicate | 250 | (250UL << 8) | (246UL << 20) | ((ulong)(array ? 3U : volume ? 4U : dimension == 0 ? 0U : 2U) << 28) |
               ((ulong)mask << 31) | ((ulong)c.Bits(90, 1) << 49) | (7UL << 51));
        TextureSupport.CopyResults(c, mask, secondDest);
        return true;
    }
}
