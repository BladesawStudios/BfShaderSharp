namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the texture gather (TLD4) for 2D and array textures, with texel offsets, per-tap offsets and half results.</summary>
/// <remarks>
/// The coordinates are gathered into R250.. and the gather writes its packed results there, which are then copied to
/// the destinations (or packed into half pairs). A texel offset becomes Maxwell AOFFI from a register; per-tap offsets
/// must be proven constants (see <see cref="GatherOffsets"/>) and are loaded into R248 and R249. A handle in a uniform
/// register makes the bindless form. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class GatherLowering
{
    public static void Register(OpcodeTable table) => table.Add(Gather, 0xb63, 0xf63);

    private sealed class Tap
    {
        public bool Bindless, Offsets, PerTap, Half, Array;
        public uint Handle, Mask, SecondDest, SourceA, SourceB, Binding, Limit, Dest;
    }

    private static Tap Decode(InstructionContext c)
    {
        bool bindless = c.Opcode == 0xf63;
        return new Tap
        {
            Bindless = bindless, Handle = bindless ? c.UniformRegister(c.Bits(40, 6)) : 255, Mask = c.Bits(72, 4), SecondDest = c.Bits(64, 8),
            SourceA = c.Bits(24, 8), SourceB = c.Bits(32, 8), Binding = c.Bits(54, 5), Offsets = c.Bits(76, 1) != 0, PerTap = c.Bits(76, 2) == 2,
            Half = c.Bits(79, 1) != 0, Array = c.Bits(61, 3) == 5, Dest = (uint)c.Dest, Limit = c.State.UsesUniformBank ? c.State.HighUniformBase : 246U,
        };
    }

    // A bindless array gather is only supported in the one operand shape the game uses.
    private static bool BadBindlessArray(InstructionContext c, Tap t)
    {
        uint count = (uint)System.Numerics.BitOperations.PopCount(t.Mask);
        uint first = t.SecondDest == 255 ? count : Math.Min(count, 2U), second = count - first;
        return c.Bits(60, 1) == 0 || (c.Insn.W2 & ~0x05800fffU) != 0x081e0000U || t.SourceA + 2 > t.Limit || t.SourceB >= t.Limit ||
               (t.Dest != 255 && t.Dest + first > t.Limit) || (t.SecondDest != 255 && t.SecondDest + second > t.Limit) ||
               (t.Dest != 255 && t.SecondDest != 255 && second != 0 && t.SecondDest < t.Dest + first && t.Dest < t.SecondDest + second) ||
               (c.Insn.W3 & 0xc00001ffU) != 0;
    }

    private static bool BadArray(InstructionContext c, Tap t) =>
        c.Bits(60, 1) == 0 || (c.Insn.W2 & ~0x058000ffU) != 0x001e0f00U || t.SourceA + 2 > t.Limit || t.SourceB >= t.Limit || t.Dest + 2 > t.Limit ||
        t.SecondDest + 2 > t.Limit || (t.SecondDest < t.Dest + 2 && t.Dest < t.SecondDest + 2) || (c.Insn.W3 & 0xc00001ffU) != 0;

    private static bool BadHalf(InstructionContext c, Tap t) =>
        t.Bindless || t.Offsets || t.PerTap || c.Bits(60, 1) == 0 || c.Bits(80, 1) != 0 || c.Bits(91, 1) != 0 || (t.SourceB != 255 && t.SourceB >= t.Limit) ||
        t.Mask != 15 || t.Dest >= t.Limit || t.SecondDest >= t.Limit;

    private static bool Invalid(InstructionContext c, Tap t)
    {
        bool binding = t.Bindless ? t.Handle >= 246 || c.Bits(46, 13) != 0 || c.Bits(91, 1) == 0
                                  : c.Bits(40, 14) > 0x1fff || t.Binding >= 18 || !c.BindTextureBuffer(t.Binding);
        return (c.Bits(61, 3) != 1 && !t.Array) || (c.Bits(77, 2) != 0 && !t.PerTap) || c.Bits(81, 3) != 7 || c.Bits(59, 1) != 0 ||
               (t.Array && !t.Bindless && BadArray(c, t)) || (t.Half && BadHalf(c, t)) ||
               (t.Offsets && (t.Bindless || t.SourceB == 255 || (t.SourceA != 255 && t.SourceA >= 245))) ||
               (t.PerTap && (t.Bindless || c.Bits(78, 3) != 0 || c.Bits(91, 1) != 0)) || c.Bits(89, 1) != 0 || t.Mask == 0 || binding;
    }

    private static bool Gather(InstructionContext c)
    {
        Tap t = Decode(c);
        if (t.Bindless && t.Array && BadBindlessArray(c, t)) return c.Fail("native uniform array texture gather mode");
        if (Invalid(c, t)) return c.Fail("native texture gather mode");
        if (t.PerTap && !LoadPerTapOffsets(c, t)) return false;
        EmitGather(c, t);
        if (t.Half) PackHalves(c, t);
        else TextureSupport.CopyResults(c, t.Mask, t.SecondDest);
        return true;
    }

    private static bool LoadPerTapOffsets(InstructionContext c, Tap t)
    {
        if (c.Bits(60, 1) == 0 || t.Mask != 15 || t.SourceA + 1 >= t.Limit || t.SourceB + 1 >= t.Limit || (t.Dest != 255 && t.Dest + 1 >= t.Limit) ||
            (t.SecondDest != 255 && t.SecondDest + 1 >= t.Limit))
            return c.Fail("native per-tap gather operands");
        uint[]? offsets = GatherOffsets.Find(c, t.SourceB);
        if (offsets == null) return false;
        for (uint i = 0; i < 2; i++) c.MovImm(248 + i, offsets[i]);
        return true;
    }

    // Two coordinates: x in Ra, y in Rb (the list splits evenly across them); with an offset, x and y in Ra, Ra+1 and the offset in Rb.
    private static void EmitGather(InstructionContext c, Tap t)
    {
        uint y = !t.Offsets && !t.PerTap ? t.SourceB : t.SourceA == 255 ? 255U : t.SourceA + 1;
        TextureSupport.Copy(c, 250, t.SourceA);
        TextureSupport.Copy(c, 251, t.Array ? t.SourceA + 1 : y);
        if (t.Array) TextureSupport.Copy(c, 252, t.SourceB);
        if (t.Offsets) TextureSupport.Copy(c, 246, t.SourceB);
        ulong type = t.Array ? 3UL : 2UL;
        if (t.Bindless)
        {
            TextureSupport.Copy(c, 246, t.Handle);
            c.Emit(0xdec0000000000000UL | c.Predicate | 250 | (250UL << 8) | (246UL << 20) | (type << 28) | ((ulong)t.Mask << 31) | ((ulong)c.Bits(87, 2) << 38) |
                   ((ulong)c.Bits(90, 1) << 49) | (7UL << 51));
            return;
        }

        uint offsetRegister = t.PerTap ? 248U : t.Offsets ? 246U : 255U;
        ulong offsetMode = t.PerTap ? 2UL : t.Offsets ? 1UL : 0UL;
        c.Emit(0xc800000000000000UL | c.Predicate | 250 | (250UL << 8) | ((ulong)offsetRegister << 20) | (type << 28) | ((ulong)t.Mask << 31) |
               ((ulong)c.Bits(40, 14) << 36) | ((ulong)c.Bits(90, 1) << 49) | (7UL << 51) | (offsetMode << 54) | ((ulong)c.Bits(87, 2) << 56));
    }

    // Half results: each pair of 32-bit results is packed into one register as two halves.
    private static void PackHalves(InstructionContext c, Tap t)
    {
        for (uint i = 0; i < 2; i++)
        {
            c.Emit(0x5ca8000000000000UL | c.Predicate | 246 | (1UL << 8) | (2UL << 10) | ((ulong)(250 + i * 2) << 20));
            c.Emit(0x5ca8000000000000UL | c.Predicate | 247 | (1UL << 8) | (2UL << 10) | ((ulong)(251 + i * 2) << 20));
            c.Emit(0x5c18000000000000UL | c.Predicate | (i == 0 ? t.Dest : t.SecondDest) | (247UL << 8) | (246UL << 20) | (16UL << 39));
        }
    }
}
