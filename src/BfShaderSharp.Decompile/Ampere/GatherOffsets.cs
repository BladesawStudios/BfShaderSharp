namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Proves the per-tap gather offsets of a texture gather are constants and finds their values.</summary>
/// <remarks>
/// The host gather takes its offsets as constants, so the two registers that carry the packed offsets must be loaded
/// with immediates earlier in the program. The backward search for them is only sound for a straight-line program, so
/// every instruction up to the terminal exit must be one of a short list of non-branching forms. Each offset byte is a
/// signed 6-bit value. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class GatherOffsets
{
    private static readonly HashSet<uint> StraightLine = [0x326, 0x802, 0x307, 0x421, 0xa20, 0xb63, 0x221, 0x823, 0x423, 0x223, 0x308, 0x220, 0x820];
    private static readonly HashSet<uint> Definitions = [0x802, 0x307, 0x421, 0xa20];

    // Returns the packed offset words for registers sourceB and sourceB+1, or null after reporting the failure.
    public static uint[]? Find(InstructionContext c, uint sourceB)
    {
        if (!IsStraightLine(c)) return null;
        uint?[] packed = new uint?[2];
        AmpereInstruction[] code = c.State.Code;
        for (uint cursor = c.Index; cursor > 0 && (packed[0] == null || packed[1] == null);)
        {
            AmpereInstruction definition = code[--cursor];
            if (definition.Bits(12, 4) == 15) continue;
            uint op = definition.Opcode, target = definition.Bits(16, 8);
            if (op == 0xb63)
            {
                if (Clobbers(definition, packed, sourceB)) return Fail(c, "native per-tap gather offset clobber");
                continue;
            }

            if (!Definitions.Contains(op)) return Fail(c, "native per-tap gather offset proof");
            for (uint i = 0; i < 2; i++)
            {
                if (packed[i] != null || target != sourceB + i) continue;
                if (op != 0x802 || definition.Bits(12, 4) != 7 || definition.Bits(24, 8) != 0 || definition.W2 != 0x00000f00)
                    return Fail(c, "native per-tap gather nonconstant offsets");
                packed[i] = definition.W1;
            }
        }

        var values = new uint[2];
        for (int i = 0; i < 2; i++)
        {
            if (packed[i] is not uint word) return Fail(c, "native per-tap gather missing offsets");
            for (int shift = 0; shift < 32; shift += 8)
            {
                uint value = (word >> shift) & 255;
                if (value > 31 && value < 224) return Fail(c, "native per-tap gather offset range");
            }

            values[i] = word;
        }

        return values;
    }

    private static uint[]? Fail(InstructionContext c, string reason)
    {
        c.Fail(reason);
        return null;
    }

    private static bool IsStraightLine(InstructionContext c)
    {
        foreach (AmpereInstruction instruction in c.State.Code)
        {
            if (instruction.Bits(12, 4) == 15) continue;
            uint op = instruction.Opcode;
            if (op == 0x94d && instruction.Bits(12, 4) == 7) return true;
            if (!StraightLine.Contains(op))
            {
                c.Fail("native per-tap gather control-flow proof");
                return false;
            }
        }

        c.Fail("native per-tap gather terminal proof");
        return false;
    }

    // Whether an earlier gather writes one of the still-unresolved offset registers.
    private static bool Clobbers(AmpereInstruction gather, uint?[] packed, uint sourceB)
    {
        uint count = (uint)System.Numerics.BitOperations.PopCount(gather.Bits(72, 4)), high = gather.Bits(64, 8), target = gather.Bits(16, 8);
        for (uint i = 0; i < 2; i++)
        {
            if (packed[i] != null) continue;
            uint register = sourceB + i;
            if ((target != 255 && register >= target && register < target + (high == 255 ? count : Math.Min(count, 2U))) ||
                (high != 255 && count > 2 && register >= high && register < high + count - 2))
                return true;
        }

        return false;
    }
}
