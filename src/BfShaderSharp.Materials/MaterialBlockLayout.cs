using BfresSharp.Model.Material;
using BfShaderSharp.Bfsha;

namespace BfShaderSharp.Materials;

/// <summary>Where each uniform of a shading model's material block sits, and the values the block has when a material sets nothing.</summary>
/// <remarks>
/// The shader declares the block: its size, a byte offset for each named uniform, and a default buffer. A material's own
/// parameter data is packed per material, so the block is built by joining the two by name: start from the defaults, then
/// write each parameter the material has at the offset the shader gives that name. A parameter the block does not
/// declare is left out. Texture transforms are the one type not copied as stored: the material holds the authored scale,
/// rotation and translation and the shader reads the baked 2x2 matrix and translation (<see cref="TexSrtBake"/>).
/// The shader stores an offset one higher than the byte it names, and a zero offset means the uniform's index times four.
/// </remarks>
public sealed class MaterialBlockLayout
{
    private readonly byte[] _defaults;
    private readonly Dictionary<string, int> _offsets;

    internal MaterialBlockLayout(string name, int size, byte[] defaults, IReadOnlyList<(string Name, int Offset)> uniforms)
    {
        Name = name;
        Size = size;
        _defaults = defaults;
        Uniforms = uniforms;
        _offsets = new Dictionary<string, int>(uniforms.Count, StringComparer.Ordinal);
        foreach ((string uniform, int offset) in uniforms) _offsets.TryAdd(uniform, offset);
    }

    public string Name { get; }

    public int Size { get; }

    public IReadOnlyList<(string Name, int Offset)> Uniforms { get; }

    /// <summary>The material block of a shading model, or null when it declares none.</summary>
    public static MaterialBlockLayout? Of(ShaderModel model)
    {
        foreach (ShaderUniformBlock block in model.UniformBlocks.All())
        {
            if (block.Type != UniformBlockType.Material || block.ByteSize == 0) continue;
            List<(string, int)> uniforms = [];
            foreach (ShaderUniform uniform in block.Uniforms.All()) uniforms.Add((uniform.Name, uniform.Offset == 0 ? uniform.Index * 4 : uniform.Offset - 1));
            return new MaterialBlockLayout(block.Name, block.ByteSize, block.DefaultBuffer.ToArray(), uniforms);
        }

        return null;
    }

    /// <summary>The block's bytes for a material; nameOf turns a parameter's key into its name.</summary>
    public byte[] Build(Material material, Func<string, string> nameOf)
    {
        byte[] block = new byte[Size];
        _defaults.AsSpan(0, Math.Min(_defaults.Length, Size)).CopyTo(block);
        ReadOnlySpan<byte> data = material.ShaderParamData;
        foreach (KeyValuePair<string, ShaderParam> pair in material.ShaderParams)
        {
            ShaderParam param = pair.Value;
            int length = param.DataSize;
            int source = param.DataOffset;
            if (!_offsets.TryGetValue(nameOf(pair.Key), out int target) || source + length > data.Length || target + length > Size) continue;
            if (param.Type is ShaderParamType.TexSrt or ShaderParamType.TexSrtEx)
            {
                byte[] baked = TexSrtBake.Bake(data.Slice(source, length));
                baked.AsSpan(0, Math.Min(baked.Length, length)).CopyTo(block.AsSpan(target));
            }
            else
            {
                data.Slice(source, length).CopyTo(block.AsSpan(target));
            }
        }

        return block;
    }
}
