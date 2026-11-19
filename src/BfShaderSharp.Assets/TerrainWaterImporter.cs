using System.Text;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Decompile;
using BfShaderSharp.Pack;
using BntxSharp;

namespace BfShaderSharp.Assets;

/// <summary>Imports the terrain water: its program from the terrain_water shader archive and the textures the program reads.</summary>
/// <remarks>
/// The program is number 98 of the archive's terrain_water shading model, stored as its vertex and fragment GLSL with the names and slots of the stage's reflection under
/// <c>TerrainWater/vert</c> and <c>TerrainWater/frag</c>. Each texture is stored as <c>TerrainWater/&lt;name&gt;</c>: its format name, size, number of array slices, and mip 0 of every slice back to back.
/// A BC4 mask is stored decoded to one byte per texel with the format R8, as the renderer reads it. The material block is not stored here; it is the block of the TranslucentNear material of the
/// model Terrain.TeraWater, which the model preparation already produces.
/// </remarks>
public static class TerrainWaterImporter
{
    public const int Program = 98;
    public static readonly string[] Textures = ["WaterAlb", "WaterNrm", "WaterEmm"];

    public static string EntryName(string part) => $"TerrainWater/{part}";

    public sealed record WaterTexture(string Format, int Width, int Height, int Layers, byte[] Bytes);

    /// <summary>Stores the program and textures; returns false when the romfs has no terrain water archive.</summary>
    public static bool Import(AssetContext context)
    {
        string? path = context.Romfs.ShaderFiles().FirstOrDefault(p => Path.GetFileName(p).StartsWith("terrain_water.", StringComparison.Ordinal) && Path.GetFileName(p).Contains("bfsha", StringComparison.Ordinal));
        if (path is null) return false;
        ShaderModel model = BfshaFile.Read(context.Romfs.Read(path)).Model("terrain_water") ?? throw new InvalidDataException("The terrain water archive has no terrain_water shading model");
        ShaderProgramBinary binary = model.Bnsh.Variation(model.Program(Program).VariationIndex).Program;
        foreach ((string part, ShaderStage stage) in new[] { ("vert", ShaderStage.Vertex), ("frag", ShaderStage.Fragment) })
            if (binary.Code(stage) is { } code)
                context.Pack.AddEntry(PackEntryKind.GameData, EntryName(part), context.Pack.Add(Encoding.UTF8.GetBytes(ReflectedGlsl.Extract(code, binary.Reflection(stage)))));

        foreach (string name in Textures)
        {
            if (context.Romfs.TextureFile(name) is not { } file) continue;
            BntxFile bntx = BntxFile.Load(context.Romfs.Read(file));
            BntxTexture texture = bntx.Textures.FirstOrDefault(t => t.Name == name) ?? bntx.Textures[0];
            bool mask = texture.Format.ToString().StartsWith("BC4", StringComparison.Ordinal);
            using MemoryStream bytes = new();
            for (int slice = 0; slice < texture.ArrayLength; slice++)
            {
                if (!mask)
                {
                    bytes.Write(texture.GetDeswizzledData(0, slice));
                    continue;
                }

                byte[] rgba = BntxTextureExtensions.ToRgba8(texture, 0, slice);
                for (int i = 0; i < texture.Width * texture.Height; i++) bytes.WriteByte(rgba[i * 4]);
            }

            RecordWriter writer = new();
            writer.String(mask ? "R8" : texture.Format.ToString());
            writer.U32((uint)texture.Width);
            writer.U32((uint)texture.Height);
            writer.U32((uint)texture.ArrayLength);
            writer.Bytes(bytes.GetBuffer().AsSpan(0, (int)bytes.Length));
            context.Pack.AddEntry(PackEntryKind.GameData, EntryName(name), context.Pack.Add(writer.ToArray()));
        }

        return true;
    }

    /// <summary>Reads a texture stored by <see cref="Import"/>.</summary>
    public static WaterTexture? Read(AssetPack pack, string name)
    {
        if (pack.GameData(EntryName(name)) is not { } stored) return null;
        RecordReader reader = new(stored);
        string format = reader.String();
        int width = (int)reader.U32(), height = (int)reader.U32(), layers = (int)reader.U32();
        return new WaterTexture(format, width, height, layers, reader.Bytes().ToArray());
    }
}
