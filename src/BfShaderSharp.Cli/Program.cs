using System.Diagnostics;
using BfresSharp;
using BfresSharp.Model;
using BfresSharp.Model.Material;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Materials;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Preparation;
using BfShaderSharp.Prepared;
using ZsDicSharp;

// bfshader list <file.bfsha[.zs]>
// bfshader prepare <file.bfsha[.zs]> <out.bfsp> [--quality <0-11>] [--threads <n>]
// bfshader show <file.bfsp> [<model> <program> <stage>]
// bfshader select <file.bfsp> <model.bfres[.zs]> [<material>]
const string Usage = "usage: bfshader list <file.bfsha[.zs]>\n       bfshader prepare <file.bfsha[.zs]> <out.bfsp> [--quality <0-11>] [--threads <n>]\n       bfshader show <file.bfsp> [<model> <program> <stage>]\n       bfshader select <file.bfsp> <model.bfres[.zs]> [<material>]";
if (args.Length < 2) return Fail(Usage);
switch (args[0])
{
    case "list":
        foreach (ShaderModel model in BfshaFile.Read(Load(args[1])).Models())
            Console.WriteLine($"{model.Name}: {model.ProgramCount} programs, {model.StaticOptions.Count + model.DynamicOptions.Count} options, {model.Samplers.Count} samplers");
        return 0;
    case "prepare" when args.Length >= 3:
        return Prepare();
    case "show":
        return Show();
    case "select" when args.Length >= 3:
        return Select();
    default:
        return Fail(Usage);
}

int Prepare()
{
    int quality = int.TryParse(Option("--quality"), out int q) ? q : 5;
    int threads = int.TryParse(Option("--threads"), out int t) ? t : 0;
    BfshaFile archive = BfshaFile.Read(Load(args[1]));
    Stopwatch clock = Stopwatch.StartNew();
    using FileStream output = File.Create(args[2]);
    PreparationResult result = ArchivePreparer.Prepare(archive, output, quality, threads, new Progress<string>(name => Console.WriteLine($"model {name}")));
    Console.WriteLine($"{result.Models} models, {result.Programs} programs, {result.Stages} stages ({result.Decompiled} distinct), {result.Failures.Count} failed, {output.Length / 1048576.0:F1} MB in {clock.Elapsed.TotalSeconds:F1} s");
    foreach (StageFailure failure in result.Failures.Take(10)) Console.WriteLine($"  {failure.Model} variation {failure.Variation} {failure.Stage}: {failure.Reason}");
    return 0;
}

int Show()
{
    PreparedArchive archive = PreparedArchive.Read(File.ReadAllBytes(args[1]));
    if (args.Length < 5)
    {
        foreach (PreparedModel model in archive.Models())
            Console.WriteLine($"{model.Name}: {model.ProgramCount} programs, {model.VariationCount} variations, {model.SamplerCount} samplers, {model.BlockCount} blocks");
        return 0;
    }

    PreparedModel? found = archive.Model(args[2]);
    if (found is null || !int.TryParse(args[3], out int program) || !Enum.TryParse(args[4], true, out ShaderStage stage)) return Fail(Usage);
    Console.WriteLine(found.Glsl(program, stage) ?? "(no such stage)");
    return 0;
}

// Prints the program each material of a model file runs, picked from the options the material assigns.
int Select()
{
    using PreparedArchive archive = PreparedArchive.Open(args[1]);
    MaterialShaderSelector selector = MaterialShaderSelector.For(archive);
    foreach (ResModel model in BfresFile.Read(Load(args[2])).Models.Values)
    foreach (Material material in model.Materials.Values.Where(m => args.Length < 4 || m.Name == args[3]))
    {
        ShaderAssign assign = material.ShaderAssign;
        int program = selector.Select(assign);
        Console.WriteLine($"{model.Name}/{material.Name}: {assign.ShaderArchiveName}/{assign.ShadingModelName} program {(program < 0 ? "none" : program)}");
    }

    return 0;
}

string? Option(string name)
{
    int at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

// A compressed file is opened with the dictionaries of the nearest ancestor romfs that has them.
static byte[] Load(string path)
{
    byte[] data = File.ReadAllBytes(path);
    if (!ZsDic.IsCompressed(data)) return data;
    for (DirectoryInfo? dir = new(Path.GetDirectoryName(Path.GetFullPath(path))!); dir is not null; dir = dir.Parent)
    {
        if (!File.Exists(Path.Combine(dir.FullName, "Pack", "ZsDic.pack.zs"))) continue;
        using ZsDic dictionaries = ZsDic.FromRomfs(dir.FullName);
        return dictionaries.Decompress(data, path);
    }

    throw new InvalidOperationException("The file is compressed and no ancestor folder has Pack/ZsDic.pack.zs");
}
