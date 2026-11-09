using BfresSharp;
using BfresSharp.Model;
using BfresSharp.Model.Material;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Lookup;
using BfShaderSharp.Materials;
using BfShaderSharp.Tests.Corpus;
using Xunit.Abstractions;
using ZsDicSharp;

namespace BfShaderSharp.Tests.Materials;

public class MaterialShaderSelectorCorpusTests(ITestOutputHelper output)
{
    private sealed record Archive(MaterialShaderSelector Selector, Dictionary<string, Dictionary<string, LookupOption>> Options, Dictionary<string, ProgramLookup> Lookups);

    // Nearly every material of the Model folder next to each Shader folder must resolve every option it assigns to an option and a
    // choice of its shading model, and find the program with exactly those choices. BFSHA_CORPUS_STRIDE keeps every n-th model file.
    [CorpusFact]
    public void MaterialsOfTheModelsFindTheirPrograms()
    {
        foreach (string shaders in GameFiles.Folders)
        {
            string romfs = Path.GetDirectoryName(shaders)!;
            string models = Path.Combine(romfs, "Model");
            if (!Directory.Exists(models)) continue;
            using ZsDic zs = ZsDic.FromRomfs(romfs);
            string table = Directory.EnumerateFiles(shaders, "ExternalBinaryString.bfres*").FirstOrDefault() ?? "";
            Dictionary<ulong, string> names = table == "" ? [] : BfresFile.Read(Decompressed(zs, table)).NamesByHash();
            Dictionary<string, Archive> archives = [];
            foreach ((string _, byte[] data) in GameFiles.BfshaFiles(shaders, stride: 1))
            {
                BfshaFile bfsha = BfshaFile.Read(data);
                Dictionary<string, Dictionary<string, LookupOption>> options = [];
                Dictionary<string, ProgramLookup> lookups = [];
                foreach (ShaderModel model in bfsha.Models())
                {
                    options[model.Name] = model.Options().ToDictionary(o => o.Name);
                    lookups[model.Name] = model.CreateLookup();
                }

                archives[bfsha.Name] = new Archive(MaterialShaderSelector.For(bfsha, names), options, lookups);
            }

            Report(Survey(models, zs, names, archives));
        }
    }

    // The first option the material declares for which the program does not have the assigned choice, or the default choice when
    // it is left at its default. Options the material does not list are set by the renderer when it draws (the skin weight count, the
    // probe and wetness options), so any program may differ in them.
    private static string? FirstMismatch(Dictionary<string, string> program, Dictionary<string, LookupOption> options, Dictionary<string, string> assigned, HashSet<string> declared)
    {
        foreach (LookupOption o in options.Values.Where(o => declared.Contains(o.Name) || assigned.ContainsKey(o.Name)))
        {
            string wanted = assigned.TryGetValue(o.Name, out string? choice) ? choice : o.Choices[o.DefaultChoice];
            if (program[o.Name] != wanted) return $"{o.Name} is {program[o.Name]} but the material wants {wanted}{(assigned.ContainsKey(o.Name) ? "" : " (default)")}";
        }

        return null;
    }

    private static string? HashedNames(string key, Dictionary<ulong, string> names) =>
        key.StartsWith("0x", StringComparison.Ordinal) && ulong.TryParse(key.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out ulong hash) ? names.GetValueOrDefault(hash) : key;

    private static byte[] Decompressed(ZsDic zs, string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return ZsDic.IsCompressed(bytes) ? zs.Decompress(bytes, path) : bytes;
    }

    private (int Files, int Skipped, int Materials, int Found, Dictionary<string, int> Problems) Survey(string models, ZsDic zs, Dictionary<ulong, string> names, Dictionary<string, Archive> archives)
    {
        int files = 0, skipped = 0, materials = 0, found = 0;
        Dictionary<string, int> problems = [];
        void Problem(string text) => problems[text] = problems.GetValueOrDefault(text) + 1;
        foreach (string path in Directory.EnumerateFiles(models, "*.bfres*").Where((_, i) => i % GameFiles.Stride == 0))
        {
            ResFile file;
            try
            {
                file = BfresFile.Read(Decompressed(zs, path));
            }
            catch (BfresSharp.Errors.BfresUnsupportedException)
            {
                skipped++;
                continue;
            }

            files++;
            foreach (Material material in file.Models.Values.SelectMany(m => m.Materials.Values))
            {
                ShaderAssign assign = material.ShaderAssign;
                materials++;
                string model = $"{assign.ShaderArchiveName}/{assign.ShadingModelName}";
                if (!archives.TryGetValue(assign.ShaderArchiveName, out Archive? archive) || !archive.Options.TryGetValue(assign.ShadingModelName, out var options))
                {
                    Problem($"no shading model {model}");
                    continue;
                }

                bool clean = true;
                foreach (var (name, choice) in MaterialShaderSelector.ChoicesOf(material, names))
                {
                    if (!options.TryGetValue(name, out LookupOption? option)) Problem($"{model}: unknown option {name}");
                    else if (!option.Choices.Contains(choice)) Problem($"{model}: option {name} has no choice {choice} (has {string.Join(",", option.Choices.Take(6))})");
                    else continue;
                    clean = false;
                }

                int named = assign.ShaderOptions.Count(o => o.Value != ShaderAssign.DefaultValue);
                if (MaterialShaderSelector.ChoicesOf(assign, names).Count != named) { Problem($"{model}: option hash not in the name table"); clean = false; }
                int program = archive.Selector.Select(material);
                if (program < 0) { Problem($"{model}: no program for {material.Name} of {Path.GetFileName(path)}"); clean = false; }
                else if (FirstMismatch(archive.Lookups[assign.ShadingModelName].ChoicesOf(program), options, MaterialShaderSelector.ChoicesOf(material, names), [.. assign.ShaderOptions.Select(o => HashedNames(o.Key, names)).OfType<string>()]) is { } mismatch) Problem($"{model}: nearest program, {mismatch}");
                if (clean) found++;
            }
        }

        return (files, skipped, materials, found, problems);
    }

    private void Report((int Files, int Skipped, int Materials, int Found, Dictionary<string, int> Problems) result)
    {
        output.WriteLine($"{result.Files} model files ({result.Skipped} unsupported), {result.Materials} materials, {result.Found} resolved every option and found a program");
        foreach (var problem in result.Problems.OrderByDescending(p => p.Value).Take(15)) output.WriteLine($"  {problem.Value}x {problem.Key}");
        // A few materials name a combination the game never compiled (cliff and rock materials of unused models), so the bar is not every material.
        Assert.True(result.Found >= result.Materials * 0.998, $"{result.Materials - result.Found} of {result.Materials} materials did not resolve");
    }
}
