using System.Numerics;

namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the texture samples that do not fit Maxwell's packed TEXS: the general operand list form.</summary>
/// <remarks>
/// The native operand list (array index, coordinates, optional handle, level or bias, offsets and depth reference) is
/// gathered into Maxwell TEX's coordinate registers from R250 and meta registers from R246, the sample goes to scratch,
/// and the packed components are copied out, or packed as halves for the FP16 form. Native packs up to four values as
/// halves of Ra and Rb (1+1, 2+1, 2+2) and five or more as 4+n. A handle in a register or a second descriptor buffer
/// makes Maxwell TEX.B, with the handle first among the meta registers. Ported from the Eden emulator
/// (GPL-3.0-or-later).
/// </remarks>
internal static class GeneralTextureSample
{
    private static readonly uint[] CoordinateCount = [1, 2, 3, 3, 2, 3, 0, 4];
    private static readonly uint[] MaxwellType = [0, 2, 4, 6, 1, 3, 0, 7];

    private sealed class Layout
    {
        public uint Coordinates, InA, SourceA, SourceB, Count;
        public bool HasLod;

        public uint Value(uint i) => i < InA ? (SourceA == 255 ? 255U : SourceA + i) : (SourceB == 255 ? 255U : SourceB + (i - InA));
    }

    public static bool Lower(InstructionContext c, uint handle)
    {
        bool bindless = c.Opcode != 0xb60;
        uint mask = c.Bits(72, 4), dimension = c.Bits(61, 3), lod = c.Bits(87, 3);
        bool offsets = c.Bits(76, 1) != 0, depthCompare = c.Bits(78, 1) != 0;
        if (dimension == 6 || c.Bits(77, 1) != 0 || c.Bits(81, 3) != 7 || c.Bits(59, 1) != (c.Opcode == 0x361 ? 1U : 0U) ||
            (!bindless && (c.Bits(40, 14) > 0x1fff || c.Bits(54, 5) >= 18)) || (lod > 3 && lod != 6 && lod != 7) || (depthCompare && mask != 1) || mask == 0)
            return false;
        Layout layout = Describe(c, dimension, lod, offsets, depthCompare);
        if ((layout.SourceA != 255 && layout.SourceA + layout.InA > 246) || (layout.Count > layout.InA && layout.SourceB != 255 && layout.SourceB + (layout.Count - layout.InA) > 246))
            return false;
        uint textureHandle = c.Opcode == 0x361 ? layout.Value(layout.Coordinates) : handle;
        if (bindless && textureHandle >= 246) return false;
        uint binding = c.Bits(54, 5);
        bool secondBuffer = !bindless && !c.BindTextureBuffer(binding);
        for (uint i = 0; i < layout.Coordinates; i++) TextureSupport.Copy(c, 250 + i, layout.Value(i));
        uint meta = GatherMeta(c, layout, textureHandle, bindless, secondBuffer, binding, offsets, depthCompare);
        ulong sample = (ulong)MaxwellType[dimension] << 28 | (ulong)mask << 31 | (ulong)c.Bits(90, 1) << 49 | (depthCompare ? 1UL << 50 : 0UL) | 7UL << 51;
        if (bindless || secondBuffer)
            c.Emit(0xde80000000000000UL | c.Predicate | 250 | (250UL << 8) | (246UL << 20) | sample | (InstructionContext.Flag(offsets) << 36) | ((ulong)lod << 37));
        else
            c.Emit(0xc000000000000000UL | c.Predicate | 250 | (250UL << 8) | ((ulong)(meta == 246 ? 255U : 246U) << 20) | sample | ((ulong)c.Bits(40, 14) << 36) |
                   (InstructionContext.Flag(offsets) << 54) | ((ulong)lod << 55));
        CopyOut(c, mask);
        return true;
    }

    // 0x361's handle is one more value after the coordinates: 2D.DC is (x, y | handle, reference), ARRAY_2D is (layer, x | y, handle).
    private static Layout Describe(InstructionContext c, uint dimension, uint lod, bool offsets, bool depthCompare)
    {
        var layout = new Layout { Coordinates = CoordinateCount[dimension], SourceA = c.Bits(24, 8), SourceB = c.Bits(32, 8) };
        layout.HasLod = lod == 2 || lod == 3 || lod == 6 || lod == 7;
        uint handleSlot = c.Opcode == 0x361 ? 1U : 0U;
        layout.Count = layout.Coordinates + handleSlot + (layout.HasLod ? 1U : 0U) + (offsets ? 1U : 0U) + (depthCompare ? 1U : 0U);
        // Unpacked 0x361 (bit 60 clear): Ra holds the coordinates, Rb the handle and the rest.
        layout.InA = c.Opcode == 0x361 && c.Bits(60, 1) == 0 ? layout.Coordinates : layout.Count <= 4 ? (layout.Count + 1) / 2 : c.Opcode == 0x361 ? layout.Coordinates : 4;
        return layout;
    }

    // Level or bias, offsets and the reference go to R246.., the handle first for the bindless forms; returns the next free meta register.
    private static uint GatherMeta(InstructionContext c, Layout layout, uint textureHandle, bool bindless, bool secondBuffer, uint binding, bool offsets, bool depthCompare)
    {
        uint meta = 246, next = layout.Coordinates + (c.Opcode == 0x361 ? 1U : 0U);
        if (bindless) TextureSupport.Copy(c, meta++, textureHandle);
        else if (secondBuffer) c.Emit(0x4c98000000070000UL | meta++ | (15UL << 39) | ((ulong)c.Bits(40, 14) << 20) | ((ulong)binding << 34));
        if (layout.HasLod) TextureSupport.Copy(c, meta++, layout.Value(next++));
        if (offsets) TextureSupport.Copy(c, meta++, layout.Value(next++));
        if (depthCompare) TextureSupport.Copy(c, meta++, layout.Value(next));
        return meta;
    }

    private static void CopyOut(InstructionContext c, uint mask)
    {
        uint secondDest = c.Bits(64, 8), dest = (uint)c.Dest, components = (uint)BitOperations.PopCount(mask);
        if (c.Bits(79, 1) == 0)
        {
            TextureSupport.CopyResults(c, mask, secondDest);
            return;
        }

        // FP16 results: (c0, c1) packed into the first destination, (c2, c3) into the second, the first of each pair in the low half.
        Pack(c, dest, 250, components > 1 ? 251U : 255U);
        if (components > 2) Pack(c, secondDest != 255 ? secondDest : (dest == 255 ? 255U : dest + 1), 252, components > 3 ? 253U : 255U);
    }

    private static void Pack(InstructionContext c, uint target, uint low, uint high)
    {
        if (target == 255) return;
        c.Emit(0x5ca8000000000000UL | c.Predicate | 246 | (1UL << 8) | (2UL << 10) | ((ulong)low << 20));
        c.Emit(0x5ca8000000000000UL | c.Predicate | 247 | (1UL << 8) | (2UL << 10) | ((ulong)high << 20));
        c.Emit(0x5c18000000000000UL | c.Predicate | target | (247UL << 8) | (246UL << 20) | (16UL << 39));
    }
}
