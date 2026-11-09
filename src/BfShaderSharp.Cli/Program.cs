using BfShaderSharp.Bfsha;
using BfShaderSharp.Preparation;
using ZsDicSharp;

// bfshader export <file.bfsha[.zs]> <outDir> [--model <name>] [--every <n>]
// bfshader list <file.bfsha[.zs]>
if (args.Length < 2 || args[0] is not ("export" or "list"))
{
    Console.Error.WriteLine("usage: bfshader export <file.bfsha[.zs]> <outDir> [--model <name>] [--every <n>]\n       bfshader list <file.bfsha[.zs]>");
    return 1;
}

BfshaFile archive = BfshaFile.Read(Load(args[1]));
if (args[0] == "list")
{
    foreach (ShaderModel model in archive.Models())
        Console.WriteLine($"{model.Name}: {model.ProgramCount} programs, {model.StaticOptions.Count + model.DynamicOptions.Count} options, {model.Samplers.Count} samplers");
    return 0;
}

if (args.Length < 3) return 1;
string? only = Option("--model");
int every = int.TryParse(Option("--every"), out int n) && n > 1 ? n : 1;
ModelExporter exporter = new(new DirectorySink(args[2]));
foreach (ShaderModel model in archive.Models().Where(m => only is null || m.Name == only))
{
    int count = exporter.Export(model, program => program % every == 0);
    Console.WriteLine($"{model.Name}: exported {count} of {model.ProgramCount} programs");
}

return 0;

string? Option(string name)
{
    int at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
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
