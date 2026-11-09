namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Maps each native opcode the lowering supports to the handler that lowers it.</summary>
/// <remarks>
/// Each family of opcodes registers its own handlers; an opcode registered twice is a programming error and is
/// refused. A handler emits the Maxwell instructions for one source instruction and returns false after recording
/// why it could not. The table is built once and shared by every lowering.
/// </remarks>
internal sealed class OpcodeTable
{
    public static readonly OpcodeTable Instance = Build();

    private readonly Dictionary<uint, Func<InstructionContext, bool>> _handlers = [];

    public void Add(Func<InstructionContext, bool> handler, params uint[] opcodes)
    {
        foreach (uint opcode in opcodes)
        {
            if (!_handlers.TryAdd(opcode, handler)) throw new InvalidOperationException($"Opcode 0x{opcode:x} has two handlers");
        }
    }

    public Func<InstructionContext, bool>? Find(uint opcode) => _handlers.GetValueOrDefault(opcode);

    private static OpcodeTable Build()
    {
        OpcodeTable table = new();
        HalfFloatLowering.Register(table);
        IntegerMultiplyLowering.Register(table);
        WideMultiplyLowering.Register(table);
        WarpLowering.Register(table);
        PredicateLowering.Register(table);
        UniformDatapathLowering.Register(table);
        GradientTextureLowering.Register(table);
        AtomicLowering.Register(table);
        SurfaceLowering.Register(table);
        BitManipulationLowering.Register(table);
        SharedMemoryLowering.Register(table);
        MatrixLowering.Register(table);
        SpecialRegisterLowering.Register(table);
        UniformCompareLowering.Register(table);
        GlobalMemoryLowering.Register(table);
        FloatToIntegerLowering.Register(table);
        TextureQueryLowering.Register(table);
        TexelFetchLowering.Register(table);
        GatherLowering.Register(table);
        TextureSampleLowering.Register(table);
        FloatArithmeticLowering.Register(table);
        AttributeLowering.Register(table);
        LocalMemoryLowering.Register(table);
        MoveLowering.Register(table);
        LeaLowering.Register(table);
        SelectLowering.Register(table);
        CompareLowering.Register(table);
        ShiftLowering.Register(table);
        IntegerToFloatLowering.Register(table);
        SpecialFunctionLowering.Register(table);
        IntegerAddLowering.Register(table);
        BranchLowering.Register(table);
        CallLowering.Register(table);
        ExitLowering.Register(table);
        StageBufferLowering.Register(table);
        return table;
    }
}
