namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Facts about native opcodes that the whole lowering relies on: which read the uniform register bank and how many registers each writes.</summary>
/// <remarks>
/// Both answers depend only on the instruction, so the program scan and the per-instruction lowering share them.
/// Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class OpcodeFacts
{
    private static readonly HashSet<uint> UniformBankOpcodes =
    [
        0xf66, 0xf6c, 0xc3e, 0x499, 0x2bf, 0x4a4, 0x987,
        0x3c4, 0x2bd, 0x896, 0xf60, 0xc11, 0xd08, 0xc0c, 0x986, 0x9a8, 0xc12, 0xc17, 0xe24, 0xc24, 0xd00, 0xd09, 0x9c3, 0xc45,
        0x2a4, 0x891, 0x890, 0x290, 0x28c, 0x88c, 0x287, 0x887, 0x958, 0xab9, 0xabb, 0x899, 0xc20, 0xe21, 0xe23, 0xc02,
        0x886, 0x3c2, 0xc82, 0x882, 0x292, 0x892, 0x291, 0x981, 0xc13, 0xc10, 0xc23, 0x526, 0x2be, 0x8a4, 0xd05,
        0x89c, 0x883, 0x2a5, 0xd06, 0xf63, 0xf69, 0xc0b, 0x8a5, 0x299, 0xc09, 0xc07, 0xc08, 0xc0a,
    ];

    public static bool ReadsUniformBank(AmpereInstruction i)
    {
        uint op = i.Opcode;
        return UniformBankOpcodes.Contains(op) ||
               ((op == 0x387 || op == 0x983) && i.Bits(91, 1) != 0) ||
               ((op == 0x984 || op == 0x988) && i.Bits(91, 1) != 0) ||
               (op == 0x81c && i.Bits(67, 1) != 0) ||
               (op == 0x83b && i.Bits(91, 1) != 0);
    }

    // The number of registers the instruction writes, or null when the opcode is not one the program scan understands.
    public static uint? WriteCount(AmpereInstruction i)
    {
        switch (i.Opcode)
        {
            case 0x202: case 0x802: case 0x424: case 0x724: case 0x824: case 0xe24: case 0x227:
            case 0x810: case 0x819: case 0x845: case 0x906: case 0x820:
            case 0x812: case 0x589:
            case 0x212: case 0x207: case 0x807: case 0x208: case 0x808: case 0xc07: case 0xc08:
            case 0x220: case 0xa20: case 0x221: case 0x421: case 0x621:
            case 0x223: case 0x423: case 0x623: case 0x823: case 0xa23:
            case 0x243: case 0x245: case 0x306:
            case 0x919: case 0xc12: case 0x300: case 0x210: case 0x308: case 0x803:
                return i.Bits(16, 8) == 255 ? 0u : 1u;
            case 0x225: case 0x625: case 0x825:
                return 2;
            case 0x305:
                return i.Bits(75, 2) == 3 ? 2u : 1u;
            case 0x83b:
                return i.Bits(72, 2) == 3 ? null : 1u << (int)i.Bits(72, 2);
            case 0x381: case 0x981: case 0x984: case 0xb82:
                uint width = i.Bits(73, 3);
                if (width > 6) return null;
                return width == 6 ? 4u : width == 5 ? 2u : 1u;
            case 0x20c: case 0x80c: case 0x81c: case 0x387: case 0x388: case 0xb1d: case 0x348: case 0x918:
            case 0x886: case 0x95b: case 0x3c2:
                return 0;
            default:
                return null;
        }
    }
}
