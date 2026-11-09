using System.Numerics;

namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the texture sample with explicit gradients (TXD), with the handle in a uniform register or a bound descriptor.</summary>
/// <remarks>
/// Maxwell TXD writes its channels to consecutive registers, so when Ampere's two destination pairs are not adjacent
/// the result goes to scratch registers and each channel is moved into place. The Ampere dimension codes 1 and 5 (2D and
/// 2D array) become Maxwell's 2 and 3. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class GradientTextureLowering
{
    public static void Register(OpcodeTable table)
    {
        table.Add(UniformHandle, 0xf6c);
        table.Add(BoundHandle, 0xb6c);
    }

    private static bool UniformHandle(InstructionContext c)
    {
        uint handle = c.UniformRegister(c.Bits(40, 6));
        uint coordinates = c.Bits(24, 8), derivatives = c.Bits(32, 8), dimension = c.Bits(61, 3), secondDestination = c.Bits(64, 8);
        uint mask = c.Bits(72, 4), channels = (uint)BitOperations.PopCount(mask), limit = c.State.HighUniformBase, destination = (uint)c.Dest;
        uint coordinateCount = dimension == 5 ? 3u : 2u;
        if ((dimension != 1 && dimension != 5) || c.Bits(46, 15) != 0 || (c.Insn.W2 & ~0x04000fffU) != 0x081e0000U || mask == 0 ||
            (c.Insn.W3 & 0xc00001ffU) != 0 || handle == uint.MaxValue || handle == 255 || coordinates + coordinateCount > limit ||
            derivatives + 4 > limit || destination + Math.Min(channels, 2U) > limit || (channels > 1 && (destination & 1) != 0) ||
            !SecondDestinationFits(channels, secondDestination, destination, limit))
            return c.Fail("native uniform gradient texture mode");
        c.Emit(0x5c98000000000000UL | c.Predicate | 248 | (15UL << 39) | ((ulong)handle << 20));
        for (uint i = 0; i < coordinateCount; i++) c.Emit(0x5c98000000000000UL | c.Predicate | (249 + i) | (15UL << 39) | ((ulong)(coordinates + i) << 20));
        c.Emit(0xde40000000000000UL | c.Predicate | 248 | (248UL << 8) | ((ulong)derivatives << 20) | ((ulong)coordinateCount << 28) | ((ulong)mask << 31) |
               ((ulong)c.Bits(90, 1) << 49) | (7UL << 51));
        for (uint i = 0; i < channels; i++)
        {
            if (i >= 2 && secondDestination == 255) break;
            c.Emit(0x5c98000000000000UL | c.Predicate | (i < 2 ? destination + i : secondDestination + i - 2) | (15UL << 39) | ((ulong)(248 + i) << 20));
        }

        return true;
    }

    private static bool SecondDestinationFits(uint channels, uint secondDestination, uint destination, uint limit)
    {
        if (channels <= 2) return secondDestination == 255;
        return secondDestination == 255 ||
               !(secondDestination + channels - 2 > limit || (channels == 4 && (secondDestination & 1) != 0) ||
                 (secondDestination < destination + 2 && destination < secondDestination + channels - 2));
    }

    // TXD 2D or ARRAY_2D, any channels; the two destination pairs anywhere.
    private static bool BoundHandle(InstructionContext c)
    {
        uint binding = c.Bits(54, 5), offset = c.Bits(40, 14), coordinates = c.Bits(24, 8), derivatives = c.Bits(32, 8);
        // Ampere dimensions: 1 = 2D, 5 = ARRAY_2D; Maxwell TXD's are 2 and 3. The coordinates (x, y[, layer]) and the four gradients sit in the same registers for both.
        uint dimension = c.Bits(61, 3);
        uint high = c.Bits(64, 8); // destination of the third and fourth channels
        // Bit 90 is forwarded (Maxwell's NODEP). Bits 72-75 are the channel mask: the first two enabled channels go to Rd, Rd+1, the others to the
        // second destination (RZ drops them; a third channel alone may be in an odd register).
        uint mask = c.Bits(72, 4), channels = (uint)BitOperations.PopCount(mask);
        ulong destination = c.Dest;
        if ((c.Insn.W2 & ~(0xfffU | (1U << 26))) != 0x001e0000 || mask == 0 || c.Bits(59, 2) != 0 || (dimension != 1 && dimension != 5) || binding >= 18 ||
            offset > 0x1fff || (channels > 1 && (destination & 1) != 0) || destination >= 248 || (channels <= 2 && high != 255) ||
            (high != 255 && ((channels > 3 && (high & 1) != 0) || high >= 248)) || coordinates >= 248 || derivatives >= 247)
            return c.Fail("native gradient texture mode");
        if (!c.BindTextureBuffer(binding)) return c.Fail("native texture descriptor buffer conflict");
        // Maxwell writes the channels to consecutive registers: when the second destination doesn't follow Rd, write them to scratch and move each into place.
        bool consecutive = channels <= 2 || high == destination + 2;
        ulong target = consecutive ? destination : 248;
        c.Emit(0xde00000000000000UL | c.Predicate | target | ((ulong)coordinates << 8) | ((ulong)derivatives << 20) | ((dimension == 5 ? 3UL : 2UL) << 28) |
               ((ulong)mask << 31) | ((ulong)offset << 36) | ((ulong)c.Bits(90, 1) << 49) | (7UL << 51));
        if (consecutive) return true;
        for (uint i = 0; i < channels; i++)
        {
            if (i >= 2 && high == 255) break;
            uint to = i < 2 ? (uint)destination + i : high + (i - 2);
            c.Emit(0x5c98000000000000UL | c.Predicate | to | (15UL << 39) | ((ulong)(248 + i) << 20));
        }

        return true;
    }
}
