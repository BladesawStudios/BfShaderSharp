namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers the multi-function unit (MUFU): reciprocal, square root, logarithm, exponent, sine and cosine.</summary>
/// <remarks>
/// Newer hardware takes sine and cosine angles in turns (compilers multiply by 1/2pi first) where Maxwell's takes
/// radians, so the source is scaled by 2pi into a scratch register. A half-precision source is widened first. The
/// source is a register, immediate, constant-buffer slot or uniform register by opcode. Ported from the Eden emulator
/// (GPL-3.0-or-later).
/// </remarks>
internal static class SpecialFunctionLowering
{
    private const uint TwoPi = 0x40c90fdb;

    public static void Register(OpcodeTable table) => table.Add(Function, 0x308, 0x908, 0xb08, 0xd08);

    private static uint ToRadians(InstructionContext c, uint source)
    {
        c.Emit(0x1e00000000000000UL | c.Predicate | 250 | ((ulong)source << 8) | ((ulong)TwoPi << 20));
        return 250;
    }

    private static bool Function(InstructionContext c)
    {
        uint opcode = c.Opcode, operation = c.Bits(74, 6);
        if (opcode == 0x908 && (c.Bits(24, 8) != 0 || (c.Insn.W2 & ~0xfc00U) != 0)) return c.Fail("native immediate MUFU mode");
        if (operation > 9 || operation == 6 || operation == 7 || (operation == 9 && c.Bits(73, 1) != 0) || c.Bits(80, 12) != (opcode == 0xd08 ? 0x800U : 0U))
            return c.Fail("native MUFU mode");
        bool turns = operation == 0 || operation == 1;
        if (c.Bits(73, 1) != 0)
        {
            if (opcode != 0x308 || c.Bits(60, 2) > 1) return c.Fail("native half special-function source");
            c.Emit(0x5ca8000000000000UL | c.Predicate | 250 | (2UL << 8) | (1UL << 10) | ((ulong)c.Bits(32, 8) << 20) | ((ulong)c.Bits(60, 1) << 41) |
                   ((ulong)c.Bits(63, 1) << 45) | ((ulong)c.Bits(62, 1) << 49));
            if (turns) ToRadians(c, 250);
            c.Emit(0x5080000000000000UL | c.Predicate | 250 | (250UL << 8) | ((ulong)operation << 20));
            c.Emit(c.Base(0x5ca8000000000000UL) | (1UL << 8) | (2UL << 10) | (250UL << 20));
            return true;
        }

        uint source = opcode == 0xd08 ? c.UniformRegister(c.Bits(32, 8)) : c.Operands.Operand(1);
        if (source == uint.MaxValue) return c.Fail("native MUFU uniform source");
        if (turns) source = ToRadians(c, source);
        c.Emit(c.Base(0x5080000000000000UL) | ((ulong)source << 8) | ((ulong)operation << 20));
        return true;
    }
}
