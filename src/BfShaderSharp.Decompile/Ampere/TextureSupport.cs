using System.Numerics;

namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Shared pieces of the texture lowerings: register copies and the result copy-out.</summary>
/// <remarks>
/// Texture instructions gather their operands into scratch registers from R250 up and leave their packed results there;
/// the results are copied to the destination pair and then the second destination pair of the source instruction.
/// </remarks>
internal static class TextureSupport
{
    public static void Copy(InstructionContext c, uint target, uint source) =>
        c.Emit(0x5c98000000000000UL | c.Predicate | target | (15UL << 39) | ((ulong)source << 20));

    // Copies the packed results in R250.. to Rd, Rd+1 and then the second destination pair.
    public static void CopyResults(InstructionContext c, uint mask, uint secondDest, uint first = 250)
    {
        uint components = (uint)BitOperations.PopCount(mask);
        bool split = secondDest != 255;
        uint dest = (uint)c.Dest;
        for (uint i = 0; i < components; i++)
        {
            uint target = !split || i < 2 ? (dest == 255 ? 255U : dest + i) : secondDest + (i - 2);
            if (target != 255) Copy(c, target, first + i);
        }
    }
}
