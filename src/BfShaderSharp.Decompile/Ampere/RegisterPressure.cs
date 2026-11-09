namespace BfShaderSharp.Decompile.Ampere;

/// <summary>An instruction rewritten to use only low registers, with the copies that bring its high registers in and out.</summary>
/// <remarks>
/// A native instruction that names a register at or above the reserved boundary is rewritten to name aligned quartets
/// borrowed from below it. <see cref="Prefix"/> copies the high registers into the borrowed ones before the instruction
/// and <see cref="Suffix"/> copies them back after it. <see cref="Valid"/> is false when the instruction cannot be
/// remapped, in which case the other members are not meaningful. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
public sealed class RegisterPressure
{
    private RegisterPressure(AmpereInstruction instruction)
    {
        Instruction = instruction;
    }

    public AmpereInstruction Instruction { get; private set; }

    public List<ulong> Prefix { get; } = [];

    public List<ulong> Suffix { get; } = [];

    public bool Valid { get; private set; } = true;

    public static RegisterPressure None(AmpereInstruction instruction) => new(instruction);

    // Preserve high guest registers in SSA while borrowing aligned low quartets for one native instruction.
    public static RegisterPressure Prepare(AmpereInstruction input, uint reserved)
    {
        RegisterPressure result = new(input);
        if (reserved < 4 || reserved > 246 || (reserved & 3) != 0) return result.Invalidate();
        List<(int Bit, uint Count)>? operands = RegisterOperands.Of(input);
        if (operands is null) return result.Invalidate();
        bool[] used = new bool[256];
        bool[] groups = new bool[64];
        foreach (var (bit, count) in operands)
        {
            uint register = input.Bits(bit, 8);
            if (register == 255) continue;
            if (register + count > 255 || (register < reserved && register + count > reserved) ||
                (register >= reserved && register / 4 != (register + count - 1) / 4))
                return result.Invalidate();
            for (uint component = 0; component < count; component++)
            {
                used[register + component] = true;
                if (register + component >= reserved) groups[(register + component) / 4] = true;
            }
        }

        uint[] mapped = new uint[64];
        for (uint group = 0; group < groups.Length; group++)
        {
            if (groups[group] && !result.Borrow(group, reserved, used, mapped)) return result.Invalidate();
        }

        result.Rewrite(operands, groups, mapped);
        return result;
    }

    private RegisterPressure Invalidate()
    {
        Valid = false;
        return this;
    }

    private bool Borrow(uint group, uint reserved, bool[] used, uint[] mapped)
    {
        uint borrow = reserved;
        while (borrow >= 4)
        {
            borrow -= 4;
            if (!used[borrow] && !used[borrow + 1] && !used[borrow + 2] && !used[borrow + 3]) break;
        }

        if (used[borrow] || used[borrow + 1] || used[borrow + 2] || used[borrow + 3]) return false;
        mapped[group] = borrow;
        for (uint component = 0; component < 4; component++)
        {
            uint low = borrow + component, high = group * 4 + component;
            used[low] = true;
            Prefix.Add(Copy(2, low, 0, low));
            Prefix.Add(Copy(0, low, 1, high));
            Suffix.Add(Copy(1, high, 0, low));
            Suffix.Add(Copy(0, low, 2, low));
        }

        return true;
    }

    private void Rewrite(List<(int Bit, uint Count)> operands, bool[] groups, uint[] mapped)
    {
        AmpereInstruction rewritten = Instruction;
        foreach (var (bit, _) in operands)
        {
            uint register = Instruction.Bits(bit, 8);
            if (register == 255 || !groups[register / 4]) continue;
            uint replacement = mapped[register / 4] + register % 4;
            int word = bit / 32, shift = bit % 32;
            rewritten[word] = (rewritten[word] & ~(255U << shift)) | (replacement << shift);
        }

        Instruction = rewritten;
    }

    private static ulong Copy(uint destinationBank, uint destination, uint sourceBank, uint source) =>
        0x5c98000000070000UL | (10UL << 44) | destination | ((ulong)destinationBank << 8) | ((ulong)sourceBank << 10) | ((ulong)source << 20);
}
