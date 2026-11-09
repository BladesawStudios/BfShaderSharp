using BfShaderSharp.Bfsha;
using BfShaderSharp.Lookup;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Container;
using BfShaderSharp.Decompile;

namespace BfShaderSharp.Preparation;

/// <summary>Builds the report of one program: option choices, bindings and the decompiled, reflected stages.</summary>
/// <remarks>
/// A stage that cannot be decompiled still appears, with its reflection and the reason in <see cref="StageReport.Error"/>,
/// so one bad stage never hides the rest of a program.
/// </remarks>
public static class ProgramExporter
{
    public static ProgramReport Build(ShaderModel model, BnshFile bnsh, ProgramLookup lookup, int program)
    {
        ShaderProgram record = model.Program(program);
        List<BindingReport> samplers = [], blocks = [];
        for (int i = 0; i < record.SamplerCount; i++)
            samplers.Add(Binding(model.Samplers.Name(i), record.Sampler(i)));
        for (int i = 0; i < record.UniformBlockCount; i++)
            blocks.Add(Binding(model.UniformBlocks.Name(i), record.UniformBlock(i)));
        ShaderProgramBinary binary = bnsh.Variation(record.VariationIndex).Program;
        List<StageReport> stages = [];
        foreach (ShaderStage stage in Enum.GetValues<ShaderStage>())
            if (binary.Code(stage) is { } code) stages.Add(Stage(code, binary.Reflection(stage)));
        return new ProgramReport(model.Name, program, record.VariationIndex, lookup.ChoicesOf(program), samplers, blocks, stages);
    }

    private static BindingReport Binding(string name, StageLocations locations) => new(name, locations.Vertex, locations.Fragment);

    private static StageReport Stage(ShaderStageCode code, StageReflection? reflection)
    {
        string? glsl = null, error = null;
        try
        {
            glsl = ShaderDecompiler.ToGlsl(code);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error = $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
        }

        if (reflection is not { } r) return new StageReport(code.Stage.ToString(), glsl, error, [], [], [], [], []);
        return new StageReport(code.Stage.ToString(), glsl, error, Slots(r.Inputs, r.InputSlot), Slots(r.Outputs, r.OutputSlot),
            Slots(r.Samplers, r.SamplerSlot), Slots(r.UniformBuffers, r.UniformBufferSlot), Slots(r.StorageBuffers, r.StorageBufferSlot));
    }

    private static List<SlotReport> Slots(ResDict names, Func<int, int> slot)
    {
        List<SlotReport> slots = new(names.Count);
        for (int i = 0; i < names.Count; i++) slots.Add(new SlotReport(names.Key(i), slot(i)));
        return slots;
    }
}
