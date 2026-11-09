namespace BfShaderSharp.Decompile.Ampere;

/// <summary>The low registers and predicates one instruction reads and writes, for the instruction forms a geometry exit setup may contain.</summary>
/// <remarks>
/// Registers are tracked as bits of a 32-bit mask, so registers 32 and above are ignored, and predicates as seven bits.
/// <see cref="Classify"/> fills the sets for the forms the setup analysis accounts for and returns false for any other
/// instruction. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal sealed class AccessSet
{
    public uint Reads { get; private set; }

    public uint Writes { get; private set; }

    public uint ReadPredicates { get; private set; }

    public uint WritePredicates { get; private set; }

    public void ReadPredicate(uint predicate)
    {
        if (predicate < 7) ReadPredicates |= 1U << (int)predicate;
    }

    public bool Classify(AmpereInstruction i)
    {
        uint opcode = i.Opcode, family = opcode & 0x1ff, form = (opcode >> 9) & 7;
        if (opcode == 0x919 || opcode == 0x923)
        {
            if (opcode == 0x923) Read(i.Bits(24, 8));
            Write(i.Bits(16, 8));
        }
        else if (opcode == 0x805 || opcode == 0xab9 || opcode == 0x945)
        {
            // These read and write nothing the setup state lives in.
        }
        else if (opcode == 0x381 || opcode == 0x981) LoadGlobal(i);
        else if (family == 0x002 && (form == 1 || form == 4 || form == 5 || form == 6))
        {
            if (form == 1) Read(i.Bits(32, 8));
            Write(i.Bits(16, 8));
        }
        else if ((family == 0x024 || family == 0x016) && form != 0)
        {
            ReadSources(i, form, true);
            if (family == 0x024 && i.Bits(74, 1) != 0) ReadPredicate(i.Bits(87, 3));
            Write(i.Bits(16, 8));
        }
        else if (family == 0x105 && (form == 1 || form == 5 || form == 6))
        {
            // F2I: register, constant buffer or uniform source in operand b; a is unused.
            if (form == 1) Read(i.Bits(32, 8));
            Write(i.Bits(16, 8));
        }
        else if (family == 0x011 && form != 0)
        {
            // LEA (.X reads the carry predicate), optional carry out.
            ReadSources(i, form, true);
            if (i.Bits(74, 1) != 0) ReadPredicate(i.Bits(87, 3));
            Write(i.Bits(16, 8));
            WritePredicate(i.Bits(81, 3));
        }
        else if (opcode == 0xb82)
        {
            // LDC with a register index; a 64-bit load writes a pair.
            if (i.Bits(24, 8) != 255) Read(i.Bits(24, 8));
            Write(i.Bits(16, 8));
            if (i.Bits(73, 3) == 5) Write(i.Bits(16, 8) + 1);
        }
        else if (family == 0x012 && form != 0)
        {
            // LOP3.LUT with an optional predicate result.
            ReadSources(i, form, true);
            Write(i.Bits(16, 8));
            WritePredicate(i.Bits(81, 3));
        }
        else if (opcode == 0x322) StoreAttribute(i);
        else if (family == 0x00c && form != 0 && i.Bits(72, 1) == 0)
        {
            ReadSources(i, form, false);
            ReadPredicate(i.Bits(87, 3));
            WritePredicate(i.Bits(81, 3));
            WritePredicate(i.Bits(84, 3));
        }
        else return false;
        return true;
    }

    private void LoadGlobal(AmpereInstruction i)
    {
        uint address = i.Bits(24, 8), size = i.Bits(73, 3);
        if (address != 255)
        {
            Read(address);
            Read(address + 1);
        }

        uint results = size < 5 ? 1 : size == 5 ? 2 : size == 6 ? 4u : 0;
        for (uint n = 0; n < results; n++) Write(i.Bits(16, 8) + n);
    }

    private void StoreAttribute(AmpereInstruction i)
    {
        // AST: stores 1-4 components from operand b for the vertex in c; writes nothing.
        uint value = i.Bits(32, 8), components = i.Bits(74, 2) + 1;
        if (value != 255)
            for (uint n = 0; n < components; n++) Read(value + n);
        Read(i.Bits(64, 8));
    }

    private void ReadSources(AmpereInstruction i, uint form, bool hasC)
    {
        Read(i.Bits(24, 8));
        if (form == 1) Read(i.Bits(32, 8));
        if (form == 2 || form == 3 || form == 7) Read(i.Bits(64, 8));
        if (hasC && (form == 1 || form == 4 || form == 5 || form == 6)) Read(i.Bits(64, 8));
    }

    private void Read(uint register)
    {
        if (register < 32) Reads |= 1U << (int)register;
    }

    private void Write(uint register)
    {
        if (register < 32) Writes |= 1U << (int)register;
    }

    private void WritePredicate(uint predicate)
    {
        if (predicate < 7) WritePredicates |= 1U << (int)predicate;
    }
}
