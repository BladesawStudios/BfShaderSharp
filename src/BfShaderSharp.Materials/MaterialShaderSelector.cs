using System.Collections.Concurrent;
using BfresSharp.Model.Material;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Lookup;
using BfShaderSharp.Prepared;

namespace BfShaderSharp.Materials;

/// <summary>Finds the program of a shader archive that a material runs, from the shading model and options the material assigns.</summary>
/// <remarks>
/// Options the material leaves at <see cref="ShaderAssign.DefaultValue"/> are not named, so they take the shader's
/// default choice. The lookup of each shading model is built on first use and kept, so selecting for many materials
/// costs one hash each. Safe to call from many threads. <see cref="Select(Material)"/> returns -1 when the archive
/// has no such shading model or no program has the assigned choices. Switch 2 files key a material's options by the
/// hash of the option name; the names come from the game's external string table (the <c>ExternalBinaryString</c>
/// file of its Shader folder), given as the hash-to-name map of that file's strings. A key that is neither a name nor
/// a hash the table knows is left out. A boolean option's True and False are the shader's choices 1 and 0. The render state, alpha test function and pass options come from the material's render infos.
/// </remarks>
public sealed class MaterialShaderSelector
{
    private readonly Func<string, ProgramLookup?> _create;
    private readonly IReadOnlyDictionary<ulong, string> _names;
    private readonly ConcurrentDictionary<string, ProgramLookup?> _lookups = new();

    private MaterialShaderSelector(Func<string, ProgramLookup?> create, IReadOnlyDictionary<ulong, string>? names)
    {
        _create = create;
        _names = names ?? new Dictionary<ulong, string>();
    }

    public static MaterialShaderSelector For(BfshaFile archive, IReadOnlyDictionary<ulong, string>? names = null) =>
        new(name => archive.Model(name)?.CreateLookup(), names);

    public static MaterialShaderSelector For(PreparedArchive archive, IReadOnlyDictionary<ulong, string>? names = null) =>
        new(name => archive.Model(name)?.CreateLookup(), names);

    public int Select(Material material)
    {
        ProgramLookup? lookup = _lookups.GetOrAdd(material.ShaderAssign.ShadingModelName, _create);
        return lookup is null ? -1 : lookup.Find(ChoicesOf(material, _names));
    }

    internal static Dictionary<string, string> ChoicesOf(Material material, IReadOnlyDictionary<ulong, string> names)
    {
        Dictionary<string, string> choices = ChoicesOf(material.ShaderAssign, names);
        RenderInfoOptions.AddTo(choices, material, names);
        return choices;
    }

    internal static Dictionary<string, string> ChoicesOf(ShaderAssign assign, IReadOnlyDictionary<ulong, string> names)
    {
        Dictionary<string, string> choices = new(assign.ShaderOptions.Count);
        foreach (KeyValuePair<string, string> option in assign.ShaderOptions)
        {
            if (option.Value != ShaderAssign.DefaultValue && HashedNames.Resolve(option.Key, names) is { } name) choices[name] = ChoiceOf(option.Value);
        }

        return choices;
    }

    // A material holds a boolean option as True or False; the shader names those choices 1 and 0.
    private static string ChoiceOf(string value) => value switch { "True" => "1", "False" => "0", _ => value };
}
