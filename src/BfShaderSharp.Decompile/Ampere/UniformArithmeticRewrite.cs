namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Rewrites native uniform-datapath arithmetic into the ordinary-register instruction it mirrors.</summary>
/// <remarks>
/// Uniform registers are kept in ordinary registers (see <see cref="InstructionContext.UniformRegister"/>), so a uniform
/// add, multiply, shift, logic or line-up instruction becomes its per-thread counterpart on the mirrored registers.
/// Only the predicated forms the lowering can express are accepted. The uniform logic instruction's predicate output
/// is computed into a scratch predicate and merged back into R239 afterwards. Ported from the Eden emulator
/// (GPL-3.0-or-later).
/// </remarks>
internal static class UniformArithmeticRewrite
{
    private static readonly Dictionary<uint, uint> Counterparts = new()
    {
        [0x2a4] = 0x224, [0x4a4] = 0x424, [0x8a4] = 0x824, [0x891] = 0x811, [0x890] = 0x810, [0x292] = 0x212,
        [0x892] = 0x812, [0x291] = 0x211, [0x896] = 0x816, [0x8a5] = 0x825, [0x290] = 0x210,
    };

    public static bool Applies(uint opcode) => Counterparts.ContainsKey(opcode);

    // Returns false after recording a failure when the instruction cannot be rewritten.
    public static bool Apply(InstructionContext c)
    {
        uint opcode = c.Opcode;
        if (c.Bits(12, 4) != 7 && opcode != 0x292 && opcode != 0x892 && !SupportedGuard(c, opcode)) return c.Fail("native uniform arithmetic predication");
        c.UniformGuard = c.Bits(12, 4) != 7;
        c.RewrittenUniform = true;
        uint destination = c.UniformRegister(c.Bits(16, 8));
        uint a = c.UniformRegister(c.Bits(24, 8));
        uint third = c.UniformRegister(c.Bits(64, 8));
        if (destination == uint.MaxValue || a == uint.MaxValue || third == uint.MaxValue) return c.Fail("native uniform arithmetic register");
        c.Insn.W0 = (c.Insn.W0 & 0xffff) | (destination << 16) | (a << 24);
        c.Insn.W2 = (c.Insn.W2 & ~0x080000ffU) | third;
        if (opcode is 0x2a4 or 0x290 or 0x292 or 0x291)
        {
            uint b = c.UniformRegister(c.Bits(32, 8));
            if (b == uint.MaxValue) return c.Fail("native uniform arithmetic second register");
            c.Insn.W1 = (c.Insn.W1 & ~0xffU) | b;
        }

        if ((opcode == 0x292 || opcode == 0x892) && c.Bits(80, 4) != 0xe)
        {
            if (c.Bits(80, 1) != 0) return c.Fail("native uniform logic predicate output");
            c.UniformOutput = c.Bits(81, 3);
            c.Insn.W2 = (c.Insn.W2 & ~(7U << 17)) | ((6 - c.UniformOutput) << 17);
        }

        c.Opcode = Counterparts[opcode];
        return true;
    }

    private static bool SupportedGuard(InstructionContext c, uint opcode)
    {
        bool add = opcode is 0x290 or 0x890 && c.Bits(74, 1) == 0 && c.Bits(81, 6) == 0x3f && c.Bits(77, 4) == 15 && c.Bits(87, 4) == 15;
        bool lea = opcode is 0x291 or 0x891 && c.Bits(74, 1) == 0 && c.Bits(80, 1) == 0 && c.Bits(81, 3) == 7;
        bool multiply = opcode == 0x2a4 && (c.Insn.W2 & ~0x080002ffU) == 0x078e0000U;
        return add || lea || multiply;
    }
}
