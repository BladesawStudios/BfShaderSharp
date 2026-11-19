using System.Buffers.Binary;
using System.Text;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Decompile;
using BfShaderSharp.Pack;

namespace BfShaderSharp.Assets;

/// <summary>Imports chosen programs of a shading model of the game's shader archives as they are, with the model's default material block.</summary>
/// <remarks>
/// A program is stored as <c>Shading/&lt;set&gt;/&lt;program&gt;/vert</c> and <c>/frag</c>, each the GLSL with the names and slots of the stage's reflection, and the immediate constants of the vertex stage as
/// <c>Shading/&lt;set&gt;/&lt;program&gt;/immediates</c> for a set that asks for them. The model's <c>gsys_material</c> block default is <c>Shading/&lt;set&gt;/material</c>.
/// </remarks>
public static class ShadingModelImporter
{
    /// <summary>One set of programs: the archive and shading model they come from, their numbers and whether the vertex stage's immediate constants are kept.</summary>
    public sealed record Set(string Name, string Archive, string Model, int[] Programs, bool Immediates = false);

    public static readonly Set Terrain = new("terrain", "terrain", "terrain", [2, 32, 62]);

    public static readonly Set Cave = new("cave", "cave", "cave_chunked_mesh_generic", [1, 2]);

    /// <summary>The Depths' ground: the quad mesh archive's generic model, whose vertex stage indexes constants at run time.</summary>
    public static readonly Set Depths = new("depths", "cave_quadmesh", "cave_quad_mesh_generic", [1, 2], true);

    public static readonly Set[] Sets = [Terrain, Cave, Depths];

    public static string EntryName(Set set, int program, string part) => $"Shading/{set.Name}/{program}/{part}";

    public static string MaterialEntry(Set set) => $"Shading/{set.Name}/material";

    /// <summary>Stores every set; returns how many were found in the romfs.</summary>
    public static int Import(AssetContext context) => Sets.Count(set => Import(context, set));

    public static bool Import(AssetContext context, Set set)
    {
        string? path = context.Romfs.ShaderFiles().FirstOrDefault(p => Path.GetFileName(p).StartsWith(set.Archive + ".", StringComparison.Ordinal) && Path.GetFileName(p).Contains("bfsha", StringComparison.Ordinal));
        if (path is null) return false;
        ShaderModel model = BfshaFile.Read(context.Romfs.Read(path)).Model(set.Model) ?? throw new InvalidDataException($"The {set.Archive} archive has no shader model \"{set.Model}\"");
        foreach (int program in set.Programs)
        {
            ShaderProgramBinary binary = model.Bnsh.Variation(model.Program(program).VariationIndex).Program;
            foreach ((string part, ShaderStage stage) in new[] { ("vert", ShaderStage.Vertex), ("frag", ShaderStage.Fragment) })
            {
                if (binary.Code(stage) is not { } code) continue;
                context.Pack.AddEntry(PackEntryKind.GameData, EntryName(set, program, part), context.Pack.Add(Encoding.UTF8.GetBytes(ReflectedGlsl.Extract(code, binary.Reflection(stage)))));
                if (set.Immediates && stage == ShaderStage.Vertex)
                    context.Pack.AddEntry(PackEntryKind.GameData, EntryName(set, program, "immediates"), context.Pack.Add(Constants(code)));
            }
        }

        ShaderUniformBlock block = model.UniformBlocks[model.UniformBlocks.IndexOf("gsys_material")];
        ReadOnlyMemory<byte> defaults = block.DefaultBuffer;
        context.Pack.AddEntry(PackEntryKind.GameData, MaterialEntry(set), context.Pack.Add(defaults.IsEmpty ? new byte[block.ByteSize] : defaults.ToArray()));
        return true;
    }

    // The stage's immediate constants: its control block keeps their size at 0x6FC and their offset into the byte code at 0x700.
    private static byte[] Constants(ShaderStageCode code)
    {
        ReadOnlySpan<byte> control = code.ControlCode.Span, bytes = code.ByteCode.Span;
        if (control.Length < 0x704) return [];
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(control[0x6FC..]), offset = BinaryPrimitives.ReadUInt32LittleEndian(control[0x700..]);
        return (ulong)offset + size > (ulong)bytes.Length ? [] : bytes.Slice((int)offset, (int)size).ToArray();
    }
}
