using BfresSharp.Model.Material;
using BfShaderSharp.Materials;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;

namespace BfShaderSharp.Assets;

/// <summary>Turns a model file's material into a material record and stores it.</summary>
/// <remarks>
/// The program is chosen here, from the options and render infos the material assigns, so the pack holds the number a
/// renderer needs and no shader tables. Hashed names of a Switch 2 file are resolved through the context's name table.
/// The texture names the samplers use are added to the context's used set. The record has no material name, so two models
/// with identical materials store one.
/// </remarks>
public static class MaterialImporter
{
    public static BlobKey Import(AssetContext context, Material material)
    {
        ShaderAssign assign = material.ShaderAssign;
        List<SamplerRecord> samplers = Samplers(context, material);
        int program = context.Shaders.Select(material);
        context.Programs.Add(assign.ShaderArchiveName, assign.ShadingModelName, program);
        (BlobKey layout, BlobKey block) = Block(context, material);
        RecordWriter writer = new();
        new MaterialRecord(
            assign.ShaderArchiveName,
            assign.ShadingModelName,
            program,
            material.Flags,
            [.. MaterialShaderSelector.ChoicesOf(material, context.Names).OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => new NamePair(c.Key, c.Value))],
            [.. material.RenderInfos.Select(i => RenderInfo(context, i))],
            samplers,
            [.. assign.SamplerAssigns.Select(a => new NamePair(Name(context, a.Key), Name(context, a.Value)))],
            [.. material.ShaderParams.Select(p => new ParamRecord(Name(context, p.Key), (byte)p.Value.Type, p.Value.DataOffset))],
            material.ShaderParamData,
            material.RenderState is { } state ? (int)state.Mode : -1,
            material.RenderState is { } blend ? (int)blend.BlendMode : -1,
            layout,
            block).Write(writer);
        return context.Pack.Add(writer.ToArray(), BlobCodec.Raw);
    }

    // The uniform block the shader expects, built from the material's parameters, and the layout of the shading model's block.
    private static (BlobKey Layout, BlobKey Block) Block(AssetContext context, Material material)
    {
        if (context.Shaders.LayoutOf(material) is not { } layout) return default;
        BlobKey layoutKey = context.LayoutKeys.GetOrAdd(layout, l =>
        {
            RecordWriter layoutWriter = new();
            new UniformLayoutRecord(l.Name, l.Size, [.. l.Uniforms.Select(u => new UniformOffset(u.Name, u.Offset))]).Write(layoutWriter);
            return context.Pack.Add(layoutWriter.ToArray(), BlobCodec.Raw);
        });
        return (layoutKey, context.Pack.Add(layout.Build(material, key => Name(context, key)), BlobCodec.Raw));
    }

    private static string Name(AssetContext context, string key) => HashedNames.NameOrKey(key, context.Names);

    private static List<SamplerRecord> Samplers(AssetContext context, Material material)
    {
        List<SamplerRecord> samplers = [];
        for (int i = 0; i < material.Samplers.Count; i++)
        {
            (string key, Sampler sampler) = (material.Samplers[i].Key, material.Samplers[i].Value);
            string texture = i < material.TextureRefs.Count ? Name(context, material.TextureRefs[i]) : "";
            if (texture != "") context.UsedTextures.TryAdd(texture, 0);
            samplers.Add(new SamplerRecord(Name(context, key), texture, (byte)sampler.WrapU, (byte)sampler.WrapV, (byte)sampler.WrapW, (byte)sampler.CompareFunc,
                (byte)sampler.Border, (byte)sampler.Anisotropy, sampler.FilterFlags, sampler.MinLod, sampler.MaxLod, sampler.LodBias));
        }

        return samplers;
    }

    private static RenderInfoRecord RenderInfo(AssetContext context, KeyValuePair<string, RenderInfo> pair)
    {
        RenderInfo info = pair.Value;
        return new RenderInfoRecord(Name(context, pair.Key), (byte)info.Type, info.Ints, info.Floats, info.Strings);
    }
}
