namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Lowers attribute loads and stores (ALD, AST), attribute interpolation (IPA) and the geometry stage's vertex emit and cut.</summary>
/// <remarks>
/// Attributes are addressed by a byte offset below 0x400 and move one to four consecutive registers. Patch attributes
/// exist only in the tessellation stages, the evaluation stage's tessellation coordinates read as a special output
/// load, and a store to the shading-rate attribute is dropped since variable-rate shading is not modelled. Geometry
/// output instructions are accepted only in a geometry stage. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal static class AttributeLowering
{
    private const uint ShadingRate = 0x5c;

    public static void Register(OpcodeTable table)
    {
        table.Add(GeometryOutput, 0x324, 0x924);
        table.Add(Attribute, 0x321, 0x322);
        table.Add(IndexedInterpolate, 0x526);
        table.Add(Interpolate, 0x326);
    }

    private static bool GeometryOutput(InstructionContext c)
    {
        if (!c.State.Options.Geometry) return c.Fail("native geometry output outside geometry stage");
        uint type = c.Bits(78, 2);
        if ((c.Insn.W2 & ~0xc000U) != 0) return c.Fail("native geometry output mode");
        if (type == 0 && (c.Opcode != 0x324 || c.Dest != 255 || c.Insn.W1 != 255)) return c.Fail("native geometry finalization mode");
        ulong handle = (ulong)c.Bits(24, 8) << 8, flags = (ulong)type << 39;
        if (c.Opcode == 0x324)
        {
            if ((c.Insn.W1 & ~0xffU) != 0) return c.Fail("native geometry output stream");
            c.Emit(c.Base(0xfbe0000000000000UL) | handle | ((ulong)c.Insn.W1 << 20) | flags);
            return true;
        }

        if (c.Insn.W1 > 0x7ffff) return c.Fail("native geometry output stream");
        c.Emit(c.Base(0xf6e0000000000000UL) | handle | ((ulong)c.Insn.W1 << 20) | flags);
        return true;
    }

    private static bool Attribute(InstructionContext c)
    {
        bool load = c.Opcode == 0x321, patch = c.Bits(76, 1) != 0, output = c.Bits(79, 1) != 0;
        uint address = c.Bits(40, 10), components = c.Bits(74, 2) + 1;
        TessellationStage stage = c.State.Options.Tessellation;
        bool coordinates = stage == TessellationStage.Evaluation && load && !patch && address >= 0x2f0 && address + components * 4 <= 0x2f8;
        if (c.Bits(77, 1) != 0 || (patch && stage == TessellationStage.None) || (output && ((!patch && !coordinates) || !load)) ||
            (patch && load && output != (stage == TessellationStage.Control)) || (patch && !load && stage != TessellationStage.Control))
            return c.Fail("native indexed/patch attribute access");
        if (address % 4 != 0 || address + components * 4 > 0x400 || c.Bits(24, 8) != 255) return c.Fail("native attribute address or indexing");
        // Ampere uses a[0x5c] for a per-primitive hint such as the shading rate; nothing reads it back, so dropping the store keeps full-rate shading.
        if (!load && address == ShadingRate && components == 1) return true;
        uint register = load ? (uint)c.Dest : c.Bits(32, 8);
        if (register == 255)
        {
            if (load) return true;
            // A store of RZ writes zeros, from zeroed scratch registers.
            for (uint i = 0; i < components; i++) c.MovImm(250 + i, 0);
            register = 250;
        }

        if (register + components > 255) return c.Fail("native attribute register range");
        uint vertex = load ? c.Bits(32, 8) : c.Bits(64, 8);
        c.Emit((load ? 0xefd8000000000000UL : 0xeff0000000000000UL) | c.Predicate | register | (255UL << 8) | ((ulong)address << 20) |
               ((ulong)(patch ? 1 : 0) << 31) | ((ulong)(output ? 1 : 0) << 32) | ((ulong)vertex << 39) | ((ulong)(components - 1) << 47));
        return true;
    }

    // IPA.PASS / CONSTANT with the attribute indexed by a uniform register; Maxwell IPA.IDX reads the whole byte address from the index register.
    private static bool IndexedInterpolate(InstructionContext c)
    {
        uint index = c.UniformRegister(c.Bits(24, 8)), attribute = c.Bits(64, 8);
        if (index == uint.MaxValue || c.Bits(32, 8) != 255 || (c.Bits(76, 4) != 0 && c.Bits(76, 4) != 4) || c.Bits(81, 3) != 7) return c.Fail("native indexed IPA mode");
        if (index == 255)
        {
            c.Emit(c.Base(0xe000000000000000UL) | (255UL << 8) | (255UL << 20) | ((ulong)attribute << 30) | ((ulong)c.Bits(78, 1) << 55));
            return true;
        }

        c.Emit(0x1c00000000000000UL | c.Predicate | 250 | ((ulong)index << 8) | ((ulong)(attribute * 4) << 20));
        c.Emit(c.Base(0xe000000000000000UL) | (250UL << 8) | (255UL << 20) | ((ulong)attribute << 30) | (1UL << 38) | ((ulong)c.Bits(78, 1) << 55));
        return true;
    }

    // IPA.PASS / CONSTANT at the default or centroid sample location: bits 76-77 are the sample mode, bit 78 the constant mode; Maxwell keeps them at 52-53 and 55.
    private static bool Interpolate(InstructionContext c)
    {
        if (c.Bits(32, 8) != 255 || c.Bits(76, 2) > 1 || c.Bits(79, 1) != 0 || c.Bits(81, 3) != 7) return c.Fail("native IPA interpolation mode");
        c.Emit(c.Base(0xe000000000000000UL) | (255UL << 8) | (255UL << 20) | ((ulong)c.Bits(64, 8) << 30) | ((ulong)c.Bits(76, 2) << 52) | ((ulong)c.Bits(78, 1) << 55));
        return true;
    }
}
