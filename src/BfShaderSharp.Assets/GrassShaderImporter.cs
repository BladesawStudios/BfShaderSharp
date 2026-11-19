using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Decompile;
using BfShaderSharp.Lookup;
using BfShaderSharp.Pack;

namespace BfShaderSharp.Assets;

/// <summary>Decompiles the G-buffer program of each kind of grass from the game's grass shader archive.</summary>
/// <remarks>
/// The archive holds three programs for each kind of vegetation, the last of which is the one that fills the G-buffer. Each kind's vertex and fragment stage is stored as game data
/// <c>Grass/&lt;kind&gt;&lt;stage&gt;</c>: the decompiled GLSL as the translator wrote it, the constants the stage's control block bakes in, the names of its uniform blocks with their slots and
/// the names of its samplers by the translator's hex name for their slot. <c>Grass/Material</c> is the shading model's default material block, which every program reads as its own, and
/// <c>Grass/Archive</c> the first sixteen hex digits of the hash of the archive.
/// </remarks>
public static class GrassShaderImporter
{
    private const string Archive = "grass";
    private const string RenderTypeOption = "grass_render_type";

    /// <summary>The grass kinds by name, with the archive's render type each is the program of.</summary>
    public static readonly (string Kind, string Type)[] Kinds =
    [
        ("Blade", "grass_blade"), ("Cross", "grass_cross"), ("Cover", "grass_cover"),
        ("Turf", "turf_blade"), ("TurfCross", "turf_cross"),
        ("FlowerPetal", "flower_petal"), ("FlowerNormal", "flower_normal"), ("FlowerLong", "flower_long"),
        ("Leaves", "fallen_leaves"), ("Shell", "fallen_shell"), ("Chips", "fallen_chips"), ("Stone", "fallen_stone"),
    ];

    public static readonly string[] Stages = ["Vertex", "Fragment"];

    public const string ArchiveEntry = "Grass/Archive";
    public const string MaterialEntry = "Grass/Material";

    public static string EntryName(string kind, string stage) => $"Grass/{kind}{stage}";

    /// <summary>One stage as stored: GLSL, the baked constants, and the blocks and samplers it names.</summary>
    public sealed record Stage(string Glsl, IReadOnlyList<float> Constants, IReadOnlyDictionary<string, int> Blocks, IReadOnlyDictionary<string, string> Samplers);

    /// <summary>Stores every kind's programs; returns false when the romfs has no grass archive.</summary>
    public static bool Import(AssetContext context)
    {
        string? path = context.Romfs.ShaderFiles().FirstOrDefault(p => Path.GetFileName(p).StartsWith(Archive + ".", StringComparison.Ordinal) && Path.GetFileName(p).Contains("bfsha", StringComparison.Ordinal));
        if (path is null) return false;
        byte[] bytes = context.Romfs.Read(path);
        BfshaFile file = BfshaFile.Read(bytes);
        ShaderModel model = file.Model("grass") ?? throw new InvalidDataException($"The grass archive has no \"{Archive}\" shader model");
        ProgramLookup lookup = model.CreateLookup();
        foreach ((string kind, string type) in Kinds)
        {
            int program = Enumerable.Range(0, model.ProgramCount).LastOrDefault(i => lookup.ChoicesOf(i).GetValueOrDefault(RenderTypeOption) == "grass_render_" + type, -1);
            if (program < 0) throw new InvalidDataException($"The grass archive has no program for \"{type}\"");
            ShaderProgramBinary binary = model.Bnsh.Variation(model.Program(program).VariationIndex).Program;
            foreach ((string stage, ShaderStage kindOfStage) in new[] { (Stages[0], ShaderStage.Vertex), (Stages[1], ShaderStage.Fragment) })
                Write(context, EntryName(kind, stage), binary.Code(kindOfStage) ?? throw new InvalidDataException($"{kind} has no {stage} stage"), binary.Reflection(kindOfStage));
        }

        ShaderUniformBlock block = model.UniformBlocks[model.UniformBlocks.IndexOf("gsys_material")];
        ReadOnlyMemory<byte> defaults = block.DefaultBuffer;
        context.Pack.AddEntry(PackEntryKind.GameData, MaterialEntry, context.Pack.Add(defaults.IsEmpty ? new byte[block.ByteSize] : defaults.ToArray()));
        context.Pack.AddEntry(PackEntryKind.GameData, ArchiveEntry, context.Pack.Add(Encoding.ASCII.GetBytes(Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant())));
        return true;
    }

    /// <summary>Reads a stage stored by <see cref="Import"/>.</summary>
    public static Stage? Read(AssetPack pack, string kind, string stage)
    {
        if (pack.GameData(EntryName(kind, stage)) is not { } bytes) return null;
        RecordReader reader = new(bytes);
        string glsl = Encoding.UTF8.GetString(reader.Bytes().Span);
        List<float> constants = reader.List(4, r => r.F32());
        Dictionary<string, int> blocks = reader.List(6, r => (r.String(), r.I32())).ToDictionary(b => b.Item1, b => b.Item2);
        Dictionary<string, string> samplers = reader.List(4, r => (r.String(), r.String())).ToDictionary(s => s.Item1, s => s.Item2);
        return new Stage(glsl, constants, blocks, samplers);
    }

    private static void Write(AssetContext context, string name, ShaderStageCode code, StageReflection? reflection)
    {
        RecordWriter writer = new();
        writer.Bytes(Encoding.UTF8.GetBytes(ShaderDecompiler.ToGlsl(code, debug: true)));
        writer.List(Constants(code), (w, value) => w.F32(value));
        List<(string, int)> blocks = [];
        List<(string, string)> samplers = [];
        if (reflection is { } r)
        {
            for (int i = 0; i < r.UniformBuffers.Count; i++)
                if (r.UniformBufferSlot(i) is >= 0 and int slot) blocks.Add((r.UniformBuffers.Key(i), slot));
            for (int i = 0; i < r.Samplers.Count; i++)
                if (r.SamplerSlot(i) is >= 0 and int slot) samplers.Add(($"{2 * slot + 8:X}", r.Samplers.Key(i)));
        }

        writer.List(blocks, (w, block) =>
        {
            w.String(block.Item1);
            w.I32(block.Item2);
        });
        writer.List(samplers, (w, sampler) =>
        {
            w.String(sampler.Item1);
            w.String(sampler.Item2);
        });
        context.Pack.AddEntry(PackEntryKind.GameData, name, context.Pack.Add(writer.ToArray()));
    }

    // The stage's immediate constants as floats: its control block keeps their size at 0x6FC and their offset into the byte code at 0x700.
    private static float[] Constants(ShaderStageCode code)
    {
        ReadOnlySpan<byte> control = code.ControlCode.Span, bytes = code.ByteCode.Span;
        if (control.Length < 0x704) return [];
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(control[0x6FC..]), offset = BinaryPrimitives.ReadUInt32LittleEndian(control[0x700..]);
        if ((ulong)offset + size > (ulong)bytes.Length) return [];
        float[] values = new float[size / 4];
        for (int i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(int)(offset + i * 4)..]);
        return values;
    }
}
