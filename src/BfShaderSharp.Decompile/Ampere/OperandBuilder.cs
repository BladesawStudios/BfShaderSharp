namespace BfShaderSharp.Decompile.Ampere;

/// <summary>A half-precision source operand: its register, lane swizzle and modifiers.</summary>
/// <remarks>The swizzle selects which half of the register supplies each lane.</remarks>
internal readonly record struct HalfSource(uint Register, uint Swizzle, bool Absolute, bool Negate);

/// <summary>Turns the source operand forms of an Ampere ALU instruction into Maxwell registers.</summary>
/// <remarks>
/// Ampere instructions take their second and third operands as a register, a 32-bit immediate, a constant-buffer
/// slot or a uniform register, chosen by the form bits of the opcode. Maxwell instructions of the lowered kind take
/// registers only in the places the lowering uses, so every other form is first moved into a scratch register
/// (R250 and up), with absolute value and negation applied there. A form that cannot be expressed sets the context's
/// operand error and yields register 255. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal sealed class OperandBuilder(InstructionContext c)
{
    // kind: 0 register, 1 immediate, 2 constant buffer, 3 uniform register.
    public uint Materialize(uint kind, uint register, uint scratch, bool absolute, bool negate)
    {
        if (kind == 3)
        {
            uint mapped = c.UniformRegister(register);
            if (mapped == uint.MaxValue)
            {
                c.OperandError = "native uniform source register";
                return 255;
            }

            register = mapped;
        }
        else if (kind == 1)
        {
            c.MovImm(scratch, c.Insn.W1);
            register = scratch;
        }
        else if (kind == 2)
        {
            if (!c.ValidCbuf())
            {
                c.OperandError = "native constant buffer address";
                return 255;
            }

            c.Emit(0x4c98000000000000UL | c.Predicate | scratch | (15UL << 39) | c.Cbuf());
            register = scratch;
        }

        if (absolute || negate)
        {
            if (register != scratch) c.Emit(0x5c98000000000000UL | c.Predicate | scratch | ((ulong)register << 20) | (15UL << 39));
            if (absolute) c.Emit(0x0400000000000000UL | c.Predicate | scratch | ((ulong)scratch << 8) | (0x7fffffffUL << 20));
            if (negate) c.Emit(0x0400000000000000UL | c.Predicate | scratch | ((ulong)scratch << 8) | (0x80000000UL << 20) | (2UL << 53));
            register = scratch;
        }

        return register;
    }

    // Operand 0 is the first source; 1 and 2 are the second and third, whose position depends on the form.
    public uint Operand(uint which, bool floating = true)
    {
        uint form = c.Bits(9, 3), register = 0, kind = 0, absolute = 0, negate = 0;
        if (which == 0)
        {
            register = c.Bits(24, 8);
            absolute = c.Bits(73, 1);
            negate = c.Bits(72, 1);
        }
        else
        {
            if (form < 1 || form > 7)
            {
                c.OperandError = "native ALU source form";
                return 255;
            }

            bool wide = which == 1 ? form is 4 or 5 or 6 : form is 2 or 3 or 7;
            if (wide)
            {
                kind = form is 6 or 7 ? 3 : form is 4 or 2 ? 1u : 2u;
                if (kind == 3) register = c.Bits(32, 8);
                if (kind is 2 or 3)
                {
                    absolute = c.Bits(62, 1);
                    negate = c.Bits(63, 1);
                }
            }
            else
            {
                bool upper = which != 1 || form is 2 or 3 or 7;
                register = c.Bits(upper ? 64 : 32, 8);
                absolute = c.Bits(upper ? 74 : 62, 1);
                negate = c.Bits(upper ? 75 : 63, 1);
            }
        }

        return Materialize(kind, register, 250 + which, floating && absolute != 0, floating && negate != 0);
    }

    public HalfSource Half(uint which)
    {
        if (which == 0) return new HalfSource(c.Bits(24, 8), c.Bits(74, 2), c.Bits(73, 1) != 0, c.Bits(72, 1) != 0);
        uint form = c.Bits(9, 3);
        bool wide = which == 1 ? form is 4 or 5 : form is 2 or 3;
        if (wide)
        {
            bool immediate = form is 2 or 4;
            uint register = Materialize(immediate ? 1u : 2u, 255, 250 + which, false, false);
            return new HalfSource(register, immediate ? 0u : c.Bits(60, 2), !immediate && c.Bits(62, 1) != 0, !immediate && c.Bits(63, 1) != 0);
        }

        bool upper = which == 2 || form is 2 or 3;
        return new HalfSource(c.Bits(upper ? 64 : 32, 8), c.Bits(upper ? 81 : 60, 2), c.Bits(upper ? 83 : 62, 1) != 0, c.Bits(upper ? 84 : 63, 1) != 0);
    }

    public void ExtractHalf(HalfSource source, uint lane, uint target)
    {
        bool high = source.Swizzle == 3 || (source.Swizzle == 0 && lane == 1);
        c.Emit(0x5ca8000000000000UL | c.Predicate | target | (2UL << 8) | ((source.Swizzle == 1 ? 2UL : 1UL) << 10) |
               ((ulong)source.Register << 20) | ((high ? 1UL : 0UL) << 41) | ((source.Negate ? 1UL : 0UL) << 45) | ((source.Absolute ? 1UL : 0UL) << 49));
    }
}
