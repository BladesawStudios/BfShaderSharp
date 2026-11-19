using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Container;

namespace BfShaderSharp.Decompile;

/// <summary>Rewrites the decompiler's GLSL the way ShaderLibrary's extractor did, so a stage reads as a program a desktop driver links: its names are the names its reflection gives, its bindings are the slots the game binds, and its immediate constants are literals.</summary>
/// <remarks>
/// Three passes run in order. The constants of the stage's constant block replace each single-component read of it (<c>fp_c1.data[2].y</c> becomes the float), the
/// binding of each sampler and block is set to the slot the game binds it at (the constant block's own declaration is dropped), and the names the translator gave
/// (<c>in_attr3</c>, <c>fp_t_tcb_8</c>, <c>fp_c4_1</c>, <c>out_attr0</c>) become the names of the stage's reflection. A stage without reflection keeps the
/// translator's names. The text is made the way the extractor made it, so a renderer whose patches were written against that text finds what it expects; the
/// lines end with a line feed.
/// </remarks>
public static partial class ReflectedGlsl
{
    private const int SamplerBaseId = 4;

    [GeneratedRegex(@"(?<stage>[vf]p)_c1\.data\[(?<index>\d+)\]\.(?<swizzle>[xyzw])(?![xyzw])", RegexOptions.CultureInvariant)]
    private static partial Regex ConstantRead();

    [GeneratedRegex(@"vp_c1_1\._m0\[(?<index>\d+)\]\.(?<swizzle>[xyzw])(?![xyzw])", RegexOptions.CultureInvariant)]
    private static partial Regex OldConstantRead();

    /// <summary>Decompiles a stage and rewrites it with its reflection and constants.</summary>
    public static string Extract(ShaderStageCode code, StageReflection? reflection)
    {
        string glsl = ShaderDecompiler.ToGlsl(code, debug: true);
        return Apply(glsl, reflection, ImmediateConstants.Read(code.ControlCode.Span, code.ByteCode.Span));
    }

    /// <summary>Rewrites decompiled GLSL with the constants and reflection of its stage.</summary>
    public static string Apply(string glsl, StageReflection? reflection, ReadOnlySpan<byte> constants)
    {
        float[] floats = new float[constants.Length / 4];
        for (int i = 0; i < floats.Length; i++) floats[i] = BitConverter.ToSingle(constants[(i * 4)..]);
        string text = Lines(glsl, line => InlineConstants(line, floats));
        text = FixBindings(text);
        return reflection is { } named ? RenameSymbols(text, named) : text;
    }

    private static string Lines(string text, Func<string, string> each)
    {
        StringBuilder builder = new(text.Length);
        foreach (string line in SplitLines(text)) builder.Append(each(line)).Append('\n');
        return builder.ToString();
    }

    private static List<string> SplitLines(string text)
    {
        List<string> lines = [];
        using StringReader reader = new(text);
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines;
    }

    private static string InlineConstants(string line, float[] constants)
    {
        if (line.Contains("vp_c1_1._m0", StringComparison.Ordinal)) line = OldConstantRead().Replace(line, match => Literal(match, constants));
        if (line.Contains("vp_c1.data[", StringComparison.Ordinal) || line.Contains("fp_c1.data[", StringComparison.Ordinal))
            line = ConstantRead().Replace(line, match => Literal(match, constants));
        return line;
    }

    // A read outside the constants is left as written.
    private static string Literal(Match match, float[] constants)
    {
        int slot = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture) * 4 + "xyzw".IndexOf(match.Groups["swizzle"].Value[0]);
        return slot < constants.Length ? constants[slot].ToString(CultureInfo.InvariantCulture) : match.Value;
    }

    // Samplers take the slot their descriptor index names; blocks take their constant buffer number less three; the constant blocks are dropped.
    private static string FixBindings(string code)
    {
        List<string> lines = SplitLines(code);
        StringBuilder builder = new(code.Length);
        int samplerBind = 0, blockBind = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.Contains("uniform sampler", StringComparison.Ordinal))
            {
                string id = line.Split('_').Last().Replace(";", "");
                int slot = Convert.ToInt32(id, 16) / 2 - SamplerBaseId;
                line = line.Replace($"binding = {samplerBind}", $"binding = {slot}", StringComparison.Ordinal);
                samplerBind++;
            }

            if (line.Contains("std140) uniform", StringComparison.Ordinal) && line.Contains("_c", StringComparison.Ordinal))
            {
                if (line.EndsWith("_fp_c1", StringComparison.Ordinal) || line.EndsWith("_vp_c1", StringComparison.Ordinal))
                {
                    i += 3;
                    continue;
                }

                int slot = Convert.ToInt32(line.Split("_c").Last().Replace(";", ""), CultureInfo.InvariantCulture);
                if (slot != 1) line = line.Replace($"binding = {slot + 1}", $"binding = {slot - 3}", StringComparison.Ordinal);
                blockBind++;
            }

            builder.Append(line).Append('\n');
        }

        _ = blockBind;
        return builder.ToString();
    }

    private static string RenameSymbols(string code, StageReflection reflection)
    {
        List<(string From, string To)> symbols = [];
        void Add(string from, string to)
        {
            if (symbols.All(s => s.From != from)) symbols.Add((from, to));
        }

        ResDict samplers = reflection.Samplers;
        for (int i = 0; i < samplers.Count; i++)
        {
            int location = reflection.SamplerSlot(i);
            if (location == -1) continue;
            string id = (location * 2 + 8).ToString("X1", CultureInfo.InvariantCulture);
            Add("vp_t_tcb_" + id, samplers.Key(i));
            Add("fp_t_tcb_" + id, samplers.Key(i));
        }

        ResDict blocks = reflection.UniformBuffers;
        for (int i = 0; i < blocks.Count; i++)
        {
            int location = reflection.UniformBufferSlot(i);
            if (location == -1) continue;
            string name = blocks.Key(i);
            Add($"_fp_c{location + 3}", "_" + name);
            Add($"_vp_c{location + 3}", "_" + name);
            Add($"fp_c{location + 3}_1", name);
            Add($"vp_c{location + 3}_1", name);
        }

        ResDict inputs = reflection.Inputs;
        for (int i = 0; i < inputs.Count; i++)
        {
            int location = reflection.InputSlot(i);
            if (location == -1) continue;
            Add($"in_attr{location}", inputs.Key(i));
        }

        ResDict outputs = reflection.Outputs;
        for (int i = 0; i < outputs.Count; i++)
        {
            int location = reflection.OutputSlot(i);
            if (location == -1) continue;
            Add($"out_attr{location}", outputs.Key(i));
        }

        return Lines(code, line =>
        {
            foreach ((string from, string to) in symbols)
                if (line.Contains(from, StringComparison.Ordinal)) line = line.Replace(from, to, StringComparison.Ordinal);
            return line;
        });
    }
}
