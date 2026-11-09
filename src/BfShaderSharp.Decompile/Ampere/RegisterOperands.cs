namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lists which register operands an instruction has, as bit offset and register count, for the opcodes whose registers can be remapped.</summary>
/// <remarks>
/// Most opcodes have a fixed list; memory, atomic and wide-result opcodes compute their register counts from the
/// instruction. An opcode that is not listed cannot have its registers remapped. Ported from the Eden emulator
/// (GPL-3.0-or-later).
/// </remarks>
internal static class RegisterOperands
{
    private static readonly Dictionary<uint, (int Bit, uint Count)[]> Fixed = BuildFixed();

    public static List<(int Bit, uint Count)>? Of(AmpereInstruction i)
    {
        uint opcode = i.Opcode;
        if (Fixed.TryGetValue(opcode, out var fixedList)) return [.. fixedList];
        return opcode switch
        {
            0x321 => [(16, i.Bits(74, 2) + 1), (24, 1), (32, 1)],
            0x322 => [(24, 1), (32, i.Bits(74, 2) + 1)],
            0x98e => [(24, i.Bits(72, 1) != 0 ? 2u : 1u), (32, i.Bits(73, 3) == 2 || i.Bits(73, 3) == 5 ? 2u : 1u)],
            0x225 or 0x227 or 0x625 or 0x825 => Multiply(opcode),
            0x83b => [(16, 1u << (int)i.Bits(72, 2)), (24, 1)],
            0x23c => [(16, 2), (24, i.Bits(75, 1) != 0 ? 4u : 2u), (32, i.Bits(75, 1) != 0 ? 2u : 1u), (64, 2)],
            0x388 or 0x984 or 0x988 => [(opcode == 0x984 ? 16 : 32, WidthCount(i)), (24, 1)],
            0x387 or 0x983 or 0x987 => [(opcode == 0x983 ? 16 : 32, WidthCount(i)), (24, 1)],
            0x381 or 0x386 or 0x981 or 0x986 =>
                [(opcode == 0x386 || opcode == 0x986 ? 32 : 16, WidthCount(i)), (24, opcode == 0x381 || opcode == 0x386 || i.Bits(90, 1) != 0 ? 2u : 1u)],
            _ => null,
        };
    }

    private static uint WidthCount(AmpereInstruction i)
    {
        uint width = i.Bits(73, 3);
        return width == 6 ? 4 : width == 5 ? 2u : 1u;
    }

    private static List<(int Bit, uint Count)> Multiply(uint opcode)
    {
        List<(int, uint)> list = [(16, opcode == 0x227 ? 1u : 2u), (24, 1)];
        if (opcode is 0x225 or 0x227)
        {
            list.Add((32, 1));
            list.Add((64, 2));
        }

        if (opcode == 0x625) list.Add((64, 1));
        if (opcode == 0x825) list.Add((64, 2));
        return list;
    }

    private static Dictionary<uint, (int, uint)[]> BuildFixed()
    {
        Dictionary<uint, (int, uint)[]> table = [];
        void Add((int, uint)[] operands, params uint[] opcodes)
        {
            foreach (uint opcode in opcodes) table[opcode] = operands;
        }

        Add([(16, 1)], 0xa02, 0xc3e, 0x802, 0xb08, 0xd08, 0x908, 0x919, 0xb05, 0xd05);
        Add([(24, 1)], 0xa0c, 0x804, 0x80b, 0x80c);
        Add([(24, 1), (32, 1)], 0x20c, 0x20b);
        Add([(16, 1), (24, 1)], 0x807, 0x808, 0x923, 0x820, 0xa20, 0x421, 0xc17, 0x817, 0x621, 0x81a, 0x433, 0x633);
        Add([(16, 1), (24, 1), (32, 1)], 0x207, 0x208, 0x220, 0x221, 0x209, 0x233);
        Add([(16, 1), (24, 1), (64, 1)], 0xa24, 0xa23, 0x623, 0x423, 0x810, 0xa10, 0xc10, 0x812, 0x824, 0x823, 0x831, 0x424, 0x624, 0x419, 0x819, 0xa11);
        Add([(16, 1), (24, 1), (32, 1), (64, 1)], 0x224, 0x223, 0x230, 0x231, 0x210, 0x211, 0x212);
        Add([(16, 1), (32, 1)], 0x308, 0x202, 0x243, 0x245, 0x305, 0x306, 0x307);
        Add([(16, 2)], 0x805);
        Add([(24, 2), (32, 1)], 0x59f);
        Add([(24, 1), (32, 1)], 0x927);
        return table;
    }
}
