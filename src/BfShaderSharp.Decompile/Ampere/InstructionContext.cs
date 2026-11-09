namespace BfShaderSharp.Decompile.Ampere;

/// <summary>The instruction being lowered, with the encoding helpers every opcode handler uses.</summary>
/// <remarks>
/// Holds a mutable copy of the source instruction (the lowering rewrites registers in the copy), its opcode, the
/// Maxwell predicate field every emitted instruction carries, and the destination register. A handler emits Maxwell
/// instructions with <see cref="Emit"/> and <see cref="Base"/>, reads operands through <see cref="Operands"/>, combines predicates with <see cref="Predicates"/>, does multi-instruction integer arithmetic with <see cref="Integer"/>, and
/// reports an unsupported form with <see cref="Fail"/> or by setting <see cref="OperandError"/>, which is checked
/// once the handler returns. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal sealed class InstructionContext
{
    public InstructionContext(LoweringState state, uint index, AmpereInstruction instruction)
    {
        State = state;
        Index = index;
        Insn = instruction;
        Opcode = instruction.Opcode;
        Operands = new OperandBuilder(this);
        Predicates = new PredicateLogic(this);
        Integer = new IntegerMath(this);
    }

    public LoweringState State { get; }

    public uint Index { get; }

    public AmpereInstruction Insn;

    public uint Opcode { get; set; }

    public OperandBuilder Operands { get; }

    public PredicateLogic Predicates { get; }

    public IntegerMath Integer { get; }

    public RegisterPressure Pressure { get; set; } = null!;

    public uint NativePredicate { get; set; }

    public bool GuardedUniform { get; set; }

    public bool UniformGuard { get; set; }

    public bool RewrittenUniform { get; set; }

    public uint GuardSlot { get; set; }

    public uint GuardMask { get; set; }

    public uint UniformOutput { get; set; } = 7;

    public uint OutputMask { get; set; }

    // The Maxwell guard field every emitted instruction carries.
    public ulong Predicate { get; set; }

    public ulong Dest { get; set; }

    public string? OperandError { get; set; }

    public int InstructionStart { get; set; }

    public int RelocationStart { get; set; }

    public List<ulong> Output => State.Result.Instructions;

    public uint Bits(int offset, int count) => Insn.Bits(offset, count);

    public bool Fail(string reason) => State.Fail(Index, reason);

    public void Emit(ulong word) => Output.Add(word);

    public ulong Base(ulong opcodeBits) => opcodeBits | Predicate | Dest;

    // NVN2 fullscreen vertex shaders read bank 24, which the game never binds; Maxwell has 18, so it is read from bank 17, also never bound.
    public uint Bank()
    {
        uint value = Insn.Bits(54, 5);
        return value == 24 ? 17U : value;
    }

    public ulong Cbuf() => ((ulong)(Insn.Bits(38, 16) / 4) << 20) | ((ulong)Bank() << 34);

    public bool ValidCbuf() => Insn.Bits(91, 1) == 0 && Bank() < 18 && Insn.Bits(38, 16) % 4 == 0;

    // Bound Maxwell texture instructions share one descriptor constant buffer per program.
    public bool BindTextureBuffer(uint binding)
    {
        LoweredProgram result = State.Result;
        if (result.RequiredTextureBuffer != uint.MaxValue && result.RequiredTextureBuffer != binding) return false;
        result.RequiredTextureBuffer = binding;
        return true;
    }

    public void MovImm(uint register, uint value) => Emit(0x0100000000000000UL | Predicate | register | (15UL << 12) | ((ulong)value << 20));

    // UR0-UR21 live in R216-R237 and UR22-UR27 in R240-R245 (R239 holds the uniform predicates), so even/odd pairs stay adjacent;
    // UR28 and up below R216. Handlers that check reg < 16 rely on UR0-UR15 being R216-R231.
    public uint UniformRegister(uint register)
    {
        if (register < 22) return 216 + register;
        if (register < 28) return 218 + register;
        if (register == 63) return 255;
        if (register < 63 && register - 28 < State.HighUniforms) return State.HighUniformBase + (register - 28);
        if (register < 63) State.Result.HighUniformRegisters = System.Math.Max(State.Result.HighUniformRegisters, (register - 26) & ~1U);
        return uint.MaxValue;
    }

    public void SavePredicate(uint mask = 1U << 6) => Emit(0x38e8000000070000UL | 253 | (255UL << 8) | ((ulong)mask << 20));

    public void RestorePredicate(uint mask = 1U << 6) => Emit(0x38f0000000070000UL | (253UL << 8) | ((ulong)mask << 20));
}
