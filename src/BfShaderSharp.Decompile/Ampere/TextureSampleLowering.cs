namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers texture sampling (TEX, TEX.B and TEX with a uniform handle) to Maxwell TEXS, TEX or TEX.B.</summary>
/// <remarks>
/// The forms that match Maxwell's packed TEXS (1D and array 2D from the program's descriptor buffer, with a level or
/// depth reference) are emitted directly; an array depth comparison goes through TEX with its coordinates gathered in
/// scratch registers; everything else is handled by <see cref="GeneralTextureSample"/>. Ported from the Eden emulator
/// (GPL-3.0-or-later).
/// </remarks>
internal static class TextureSampleLowering
{
    private static readonly uint[] RgMasks = [1, 2, 4, 8, 3, 9, 10, 12];
    private static readonly uint[] RgbaMasks = [7, 11, 13, 14, 15];

    public static void Register(OpcodeTable table) => table.Add(Sample, 0xb60, 0xf60, 0x361);

    private static bool Sample(InstructionContext c)
    {
        bool bindless = c.Opcode != 0xb60;
        uint handle = c.Opcode == 0xf60 ? c.UniformRegister(c.Bits(40, 6)) : 255;
        if (bindless && ((c.Opcode == 0xf60 && (handle == uint.MaxValue || handle >= 246 || c.Bits(46, 13) != 0)) ||
                         (c.Opcode == 0x361 && (c.Bits(40, 19) != 0 || c.Bits(87, 3) > 3))))
            return c.Fail("native bindless texture mode");
        uint mask = c.Bits(72, 4), secondDest = c.Bits(64, 8), dimension = c.Bits(61, 3), lod = c.Bits(87, 3);
        bool split = secondDest != 255, depthCompare = c.Bits(78, 1) != 0;
        uint required = c.State.Result.RequiredTextureBuffer;
        if (!bindless && dimension == 5 && depthCompare && lod == 0 && !split && mask == 1 && (required == uint.MaxValue || required == c.Bits(54, 5)))
            return DepthArray(c);
        uint swizzle = uint.MaxValue;
        uint[] masks = split ? RgbaMasks : RgMasks;
        for (uint i = 0; i < masks.Length; i++)
            if (masks[i] == mask) swizzle = i;
        if (!UsesPackedForm(c, bindless, swizzle, mask, secondDest, depthCompare))
            return GeneralTextureSample.Lower(c, handle) || c.Fail("native texture mode or descriptor binding");
        ulong sampleMode = dimension == 5 ? (lod != 0 ? 8UL : 7UL) : depthCompare ? (lod != 0 ? 6UL : 4UL) : (lod == 3 ? 3UL : lod != 0 ? 2UL : 1UL);
        c.Emit((0xd800000000000000UL & ~((ulong)c.Bits(79, 1) << 59)) | c.Predicate | c.Dest | ((ulong)c.Bits(24, 8) << 8) | ((ulong)c.Bits(32, 8) << 20) |
               ((ulong)c.Bits(64, 8) << 28) | ((ulong)c.Bits(40, 14) << 36) | ((ulong)c.Bits(90, 1) << 49) | ((ulong)swizzle << 50) | (sampleMode << 53));
        return true;
    }

    // An array depth comparison with one result: TEX with the layer, x, y gathered in R250.. and the reference after the last source.
    private static bool DepthArray(InstructionContext c)
    {
        uint sourceA = c.Bits(24, 8), sourceB = c.Bits(32, 8);
        if (c.Bits(60, 1) == 0 || c.Bits(76, 2) != 0 || c.Bits(81, 3) != 7 || c.Bits(59, 1) != 0 || c.Bits(40, 14) > 0x1fff || c.Bits(54, 5) >= 18 ||
            (sourceA & 1) != 0 || (sourceB & 1) != 0 || sourceA >= 248 || sourceB >= 248 || c.Dest >= 248)
            return c.Fail("native array depth comparison sample mode");
        if (!c.BindTextureBuffer(c.Bits(54, 5))) return c.Fail("native texture descriptor buffer conflict");
        for (uint i = 0; i < 3; i++) TextureSupport.Copy(c, 250 + i, i < 2 ? sourceA + i : sourceB);
        c.Emit(0xc000000000000000UL | c.Predicate | c.Dest | (250UL << 8) | ((ulong)(sourceB + 1) << 20) | (3UL << 28) | (1UL << 31) |
               ((ulong)c.Bits(40, 14) << 36) | ((ulong)c.Bits(90, 1) << 49) | (1UL << 50) | (7UL << 51));
        return true;
    }

    // SM70 scalar coordinates and split RG/BA destinations match TEXS; descriptor offsets are words in both encodings, but only the native one names the buffer.
    private static bool UsesPackedForm(InstructionContext c, bool bindless, uint swizzle, uint mask, uint secondDest, bool depthCompare)
    {
        uint dimension = c.Bits(61, 3), lod = c.Bits(87, 3), sourceA = c.Bits(24, 8), dest = (uint)c.Dest;
        bool split = secondDest != 255;
        bool rejected = bindless || c.Bits(60, 1) == 0 || (dimension != 1 && dimension != 5) || swizzle == uint.MaxValue || c.Bits(76, 2) != 0 ||
            c.Bits(81, 3) != 7 || (lod > 1 && (lod != 3 || dimension != 1 || depthCompare)) || (lod == 3 && ((sourceA & 1) != 0 || sourceA >= 249)) ||
            c.Bits(59, 1) != 0 || c.Bits(40, 14) > 0x1fff || c.Bits(54, 5) != 2 || dest == 255 ||
            ((mask & (mask - 1)) != 0 && ((dest & 1) != 0 || dest >= 254)) || (split && mask == 15 && ((secondDest & 1) != 0 || secondDest >= 254)) ||
            (depthCompare && (dimension != 1 || split || mask != 1 || (sourceA & 1) != 0 || sourceA >= 249)) ||
            (dimension == 5 && ((sourceA & 1) != 0 || sourceA >= 249));
        return !rejected && c.BindTextureBuffer(2);
    }
}
