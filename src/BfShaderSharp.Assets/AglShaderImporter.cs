using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Decompile;
using BfShaderSharp.Errors;
using BfShaderSharp.Pack;
using BfShaderSharp.Sharc;
using SarcSharp;

namespace BfShaderSharp.Assets;

/// <summary>Decompiles the engine's own screen shaders, the programs of Tears of the Kingdom's agl SHARC archives, to GLSL: one vertex and one pixel text for each combination of a program's macros that a renderer runs.</summary>
/// <remarks>
/// The programs are the HDR compose, the gloom renderer's three passes, the cloud, the lens flare and the sky post-fx chain with its precompute steps. Each text is game data
/// <c>Agl/&lt;name&gt;.vert</c> or <c>.frag</c> with names the renderer asks for (<c>agl_&lt;program&gt;</c>, then <c>_&lt;macro&gt;&lt;value&gt;</c> for each macro of a combination). The decompiled code reads the
/// immediate constant buffer from a block nothing binds, so those reads are replaced with the values of the stage's control block. A combination that cannot be decompiled is left out
/// and reported by name.
/// </remarks>
public static class AglShaderImporter
{
    private const string ApplicationPackage = "Shader/ApplicationPackage.Nin_NX_NVN.release.sarc.zs";
    private const string AglResource = "Lib/agl/agl_resource.Nin_NX_NVN.release.sarc.zs";

    // Both sky variants must match the bake, which stores the sun view non-linearly.
    private const string NonLinear = "1";

    private static readonly string[] SkyPrecompute =
    [
        "sky_transmittance", "sky_irradiance", "sky_inscatter", "sky_delta_inscatter",
        "sky_copy_inscatter", "sky_copy_irradiance", "sky_bake_irradiance", "sky_bake_range_transmittance",
    ];

    private static readonly string[] GloomPrograms = ["miasma_renderer_shape", "miasma_renderer_count", "miasma_renderer_compose"];

    public const string Prefix = "Agl/";

    public static string EntryName(string name, string extension) => $"{Prefix}{name}.{extension}";

    /// <summary>Stores every program; returns the number of texts stored and the combinations that failed.</summary>
    public static (int Stored, List<string> Failed) Import(AssetContext context)
    {
        Run run = new(context);
        string application = Path.Combine(context.Romfs.Root, ApplicationPackage), resource = Path.Combine(context.Romfs.Root, AglResource);
        if (!File.Exists(application) || !File.Exists(resource)) return (0, []);

        SharcbFile shader = run.Open(application, "AglShader.sharcb");
        run.AllVariants(shader, shader.Program("hdr_compose")!);
        foreach (string extension in new[] { "vert", "frag" })
            if (run.Texts.TryGetValue(EntryName("agl_hdr_compose_ENABLE_COLOR_CORRECTION_TABLE0", extension), out byte[]? bare))
                run.Add(EntryName("agl_hdr_compose", extension), bare);
        foreach (string name in GloomPrograms) run.AllVariants(shader, shader.Program(name)!);

        SharcbFile technique = run.Open(resource, "agl_technique.sharcb");
        run.Variant(technique, "cloud", new()
        {
            ["TYPE_USE_TEX_BLEND"] = "1",
            ["TYPE_USE_DEBUG_SUN_DISP"] = "0",
            ["TYPE_USE_PROC_TEXTURE"] = "0",
            ["TYPE_USE_SCATTER"] = "1",
            ["TYPE_USE_NLD_SOFTPTCL"] = "0",
        }, "agl_cloud");

        SharcbFile effects = run.Open(resource, "agl_technique_pfx.sharcb");
        run.Variant(effects, "flare_filter_flare", new() { ["GHOST_NUM"] = "4", ["IS_HALO"] = "1", ["IS_DISTORTION"] = "0" }, "agl_flare_filter_flare");
        foreach (string direction in new[] { "0", "1" })
            run.Variant(effects, "glare_filter_blur", new() { ["BLUR_DIR"] = direction, ["BLUR_LV"] = "3", ["COLOR_BIAS"] = "1", ["IS_FLIP"] = "0" }, $"agl_glare_filter_blur{direction}");

        foreach (var (fog, sun, name) in new[] { ("0", "0", "sky"), ("0", "1", "sky_sun"), ("1", "0", "sky_fog"), ("1", "1", "sky_sun_fog") })
            run.Variant(effects, "sky_postfx_sky", new()
            {
                ["BAKED_SUNVIEW_NON_LINEAR"] = NonLinear, ["USE_ADHOC_FOG"] = fog, ["RENDER_SUN"] = sun,
                ["RENDER_CLOUD"] = "0", ["USE_TONEMAP"] = "0",
            }, $"agl_sky_postfx_{name}");

        run.Variant(effects, "sky_postfx_ground", new()
        {
            ["BAKED_SUNVIEW_NON_LINEAR"] = NonLinear, ["USE_ADHOC_FOG"] = "0", ["USE_LINEAR_DEPTH"] = "1",
            ["USE_FOG_DENSITY"] = "0", ["USE_TONEMAP"] = "0",
        }, "agl_sky_postfx_ground");

        // Each LOCAL_STEP is one iteration of the offline solve, so every declared value is a pass the chain needs.
        foreach (string name in SkyPrecompute)
        {
            SharcProgram program = effects.Program(name)!;
            if (program.Macros.FirstOrDefault(m => m.Name == "LOCAL_STEP") is not { } steps) run.Variant(effects, name, new(), $"agl_{name}");
            else
                foreach (string step in steps.Values) run.Variant(effects, name, new() { ["LOCAL_STEP"] = step }, $"agl_{name}_step{step}");
        }

        run.Variant(effects, "sky_bake_inscatter", new() { ["LOCAL_STEP"] = "0", ["BAKED_SUNVIEW_NON_LINEAR"] = NonLinear, ["ADHOC_PROC"] = "0" }, "agl_sky_bake_inscatter");
        foreach ((string name, byte[] text) in run.Texts.OrderBy(t => t.Key, StringComparer.Ordinal)) context.Pack.AddEntry(PackEntryKind.GameData, name, context.Pack.Add(text));
        return (run.Texts.Count, run.Failed);
    }

    private sealed class Run(AssetContext context)
    {
        public Dictionary<string, byte[]> Texts { get; } = new(StringComparer.Ordinal);

        public List<string> Failed { get; } = [];

        public void Add(string name, byte[] text) => Texts[name] = text;

        public SharcbFile Open(string package, string entry) =>
            SharcbFile.Read(SarcFile.FromBinary(context.Romfs.Read(package))[entry]?.Data ?? throw new InvalidDataException($"'{package}' has no \"{entry}\" entry"));

        // One combination, which must exist and yield stages.
        public void Variant(SharcbFile file, string program, Dictionary<string, string> choices, string name)
        {
            SharcProgram found = file.Program(program) ?? throw new InvalidDataException($"No \"{program}\" program");
            foreach ((string macro, string value) in choices)
            {
                SharcMacro declared = found.Macros.FirstOrDefault(m => m.Name == macro) ?? throw new InvalidDataException($"Program \"{program}\" declares no macro \"{macro}\"");
                if (!declared.Values.Contains(value)) throw new InvalidDataException($"Program \"{program}\" macro \"{macro}\" has no value \"{value}\"");
            }

            if (WriteStages(file, found, found.VariationIndex(choices), name) == 0) throw new InvalidDataException($"Program \"{program}\" combination yielded no byte code");
        }

        // Every combination, the first macro changing slowest; one that fails is reported.
        public void AllVariants(SharcbFile file, SharcProgram program)
        {
            for (int index = 0; index < program.VariationCount; index++)
            {
                string suffix = program.Macros.Count > 0 ? "_" + string.Join("_", Choices(program, index).Select(c => $"{c.Key}{c.Value}")) : "";
                try
                {
                    WriteStages(file, program, index, $"agl_{program.Name}{suffix}");
                }
                catch (Exception ex) when (ex is BfShaderFormatException or BfShaderUnsupportedException or InvalidDataException)
                {
                    Failed.Add($"{program.Name}{suffix}: {ex.Message}");
                }
            }
        }

        private static List<KeyValuePair<string, string>> Choices(SharcProgram program, int index)
        {
            List<KeyValuePair<string, string>> choices = [];
            for (int i = program.Macros.Count - 1; i >= 0; i--)
            {
                SharcMacro macro = program.Macros[i];
                choices.Insert(0, new(macro.Name, macro.Values[index % macro.Values.Count]));
                index /= macro.Values.Count;
            }

            return choices;
        }

        private int WriteStages(SharcbFile file, SharcProgram program, int variation, string name)
        {
            int written = 0;
            foreach (SharcStage stage in program.Stages.Where(s => s is SharcStage.Vertex or SharcStage.Pixel))
            {
                SharcVariation compiled = file.Variation(program, variation, stage);
                if (compiled.ByteCode.Length == 0) continue;
                Add(EntryName(name, stage == SharcStage.Pixel ? "frag" : "vert"), Encoding.UTF8.GetBytes(Fold(ShaderDecompiler.ToGlsl(compiled.ToStageCode()), Constants(compiled))));
                written++;
            }

            return written;
        }
    }

    // The immediate constants of a Switch stage as floats: the control block keeps their size at 0x6FC and their offset into the byte code at 0x700.
    private static float[] Constants(SharcVariation variation)
    {
        ReadOnlySpan<byte> control = variation.ControlCode.Span, code = variation.ByteCode.Span;
        if (control.Length < 0x704) return [];
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(control[0x6FC..]), offset = BinaryPrimitives.ReadUInt32LittleEndian(control[0x700..]);
        if ((ulong)offset + size > (ulong)code.Length) return [];
        float[] values = new float[size / 4];
        for (int i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadSingleLittleEndian(code[(int)(offset + i * 4)..]);
        return values;
    }

    // The immediate constant buffer is baked into the shader's code; leaving its reads dangling references a block nothing binds.
    private static string Fold(string code, float[] constants)
    {
        if (constants.Length == 0) return code;
        Dictionary<string, float> literals = [];
        for (int i = 0; i < constants.Length; i++)
        {
            string component = "xyzw"[i % 4].ToString();
            literals[$"vp_c1.data[{i / 4}].{component}"] = constants[i];
            literals[$"fp_c1.data[{i / 4}].{component}"] = constants[i];
            literals[$"vp_c1_1._m0[{i / 4}].{component}"] = constants[i];
        }

        StringBuilder folded = new();
        foreach (string line in code.Split('\n'))
        {
            string text = line;
            if (text.Contains("_c1.data[") || text.Contains("_c1_1._m0["))
                foreach ((string key, float value) in literals.Where(kv => text.Contains(kv.Key))) text = text.Replace(key, value.ToString("R", CultureInfo.InvariantCulture));
            folded.Append(text).Append('\n');
        }

        return folded.ToString();
    }
}
