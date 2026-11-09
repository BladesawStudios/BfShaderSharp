using BfresSharp.Model.Material;

namespace BfShaderSharp.Materials;

/// <summary>The shader options that a material sets through its render infos instead of its option list.</summary>
/// <remarks>
/// The gsys material system turns the render state of a material into options of its shaders: the render state mode,
/// the alpha test function and the render pass. A render info holds the value as a word, and the option's choice is the
/// position of that word in the engine's list. A render info with a word the list does not have sets nothing.
/// </remarks>
internal static class RenderInfoOptions
{
    private sealed record Rule(string RenderInfo, string Option, string[] Words);

    private static readonly Rule[] Rules =
    [
        new("gsys_render_state_mode", "gsys_renderstate", ["opaque", "mask", "translucent", "custom"]),
        new("gsys_alpha_test_func", "gsys_alpha_test_func", ["never", "less", "equal", "lequal", "greater", "notequal", "gequal", "always"]),
        new("gsys_pass", "gsys_pass", ["no_setting", "seal", "xlu_water"]),
        new("gsys_render_state_display_face", "gsys_display_face_type", ["both", "front", "back", "none"]),
        new("gsys_alpha_test_enable", "gsys_alpha_test_enable", ["false", "true"]),
    ];

    public static void AddTo(Dictionary<string, string> choices, Material material, IReadOnlyDictionary<ulong, string> names)
    {
        foreach (KeyValuePair<string, RenderInfo> info in material.RenderInfos)
        {
            if (info.Value.Type != RenderInfoType.String || info.Value.Strings.Length != 1) continue;
            string? name = HashedNames.Resolve(info.Key, names);
            Rule? rule = Rules.FirstOrDefault(r => r.RenderInfo == name);
            int choice = rule is null ? -1 : Array.IndexOf(rule.Words, info.Value.Strings[0]);
            if (choice >= 0) choices[rule!.Option] = choice.ToString();
        }
    }
}
