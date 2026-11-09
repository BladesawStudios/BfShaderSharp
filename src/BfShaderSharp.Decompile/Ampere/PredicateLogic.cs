namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Computes three-input predicate functions with Maxwell instructions that only take registers.</summary>
/// <remarks>
/// A predicate operand is an index with bit 3 set for negation. Each input becomes 0 or all ones in a scratch register,
/// and one LOP3 sets the destination predicate when the lookup result is nonzero; a second destination with its own
/// table is computed the same way. A uniform predicate input is read from bit 6-n of R239. Ported from the Eden
/// emulator (GPL-3.0-or-later).
/// </remarks>
internal sealed class PredicateLogic(InstructionContext c)
{
    public void Lut(uint lut, uint a, uint b, uint third, uint target, bool uniformThird = false, uint secondTarget = 7, uint secondLut = 0)
    {
        uint[] inputs = [a, b, third];
        for (uint i = 0; i < 3; i++)
        {
            if (i == 2 && uniformThird)
            {
                LoadUniformThird(third);
                if ((third & 8) != 0)
                {
                    lut = ((lut & 0x55) << 1) | ((lut & 0xaa) >> 1);
                    secondLut = ((secondLut & 0x55) << 1) | ((secondLut & 0xaa) >> 1);
                }

                continue;
            }

            c.Emit(0x38a0000000000000UL | c.Predicate | (250 + i) | (255UL << 8) | (0x7ffffUL << 20) | (1UL << 56) | ((ulong)(inputs[i] ^ 8U) << 39));
        }

        if (secondTarget != 7)
        {
            Both(lut, secondLut, target, secondTarget);
            return;
        }

        c.Emit(0x5be0000000000000UL | c.Predicate | 255 | (250UL << 8) | (251UL << 20) | ((ulong)lut << 28) | (3UL << 36) | (252UL << 39) | ((ulong)target << 48));
    }

    // UPn from R239 bit 6-n as 0 or all ones (UPT is all ones).
    private void LoadUniformThird(uint third)
    {
        if ((third & 7) == 7)
        {
            c.Emit(0x0100000000000000UL | c.Predicate | 252 | (15UL << 12) | ((ulong)uint.MaxValue << 20));
            return;
        }

        c.Emit(0x3800000000000000UL | c.Predicate | 252 | (239UL << 8) | ((ulong)((6 - (third & 7)) | (1U << 8)) << 20));
        c.Emit(0x5c10000000000000UL | c.Predicate | 252 | (255UL << 8) | (252UL << 20) | (1UL << 48));
    }

    private void Both(uint lut, uint secondLut, uint target, uint secondTarget)
    {
        uint firstMask = target == 7 ? 0U : 1U << (int)target;
        uint secondMask = 1U << (int)secondTarget;
        for (uint i = 0; i < 2; i++)
        {
            uint register = 248 + i;
            c.Emit(0x5be0000000000000UL | c.Predicate | register | (250UL << 8) | (251UL << 20) | ((ulong)(i == 0 ? lut : secondLut) << 28) |
                   (252UL << 39) | (7UL << 48));
            c.Emit(0x0400000000000000UL | c.Predicate | register | ((ulong)register << 8) | ((ulong)(i == 0 ? firstMask : secondMask) << 20));
        }

        c.Emit(0x5c40000000000000UL | c.Predicate | 248 | (248UL << 8) | (249UL << 20) | (1UL << 41) | (7UL << 48));
        c.Emit(0x38f0000000000000UL | c.Predicate | (248UL << 8) | ((ulong)(firstMask | secondMask) << 20));
    }
}
