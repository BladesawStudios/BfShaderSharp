using System.Diagnostics;
using BfShaderSharp.Assets;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using BfShaderSharp.Preparation;

namespace BfShaderSharp.Cli;

// bfshader assets: prepares a romfs's models, materials and textures into one pack, and with --shaders the shader archives into prepared files at the same time.
internal static class AssetsCommand
{
    internal static int Run(string[] args)
    {
        string[] patterns = Values(args, "--model");
        int threads = int.TryParse(Values(args, "--threads").FirstOrDefault(), out int t) ? t : 0;
        TextureSelection textures = args.Contains("--all-textures") ? TextureSelection.All : args.Contains("--no-textures") ? TextureSelection.None : TextureSelection.Used;
        AssetOptions options = new(args[1], patterns, textures, threads);
        string? shaders = Values(args, "--shaders").FirstOrDefault();
        Stopwatch clock = Stopwatch.StartNew();
        Task<string>? shaderTask = null;
        using FileStream output = File.Create(args[2]);
        // The shader archives are prepared for the programs the models use, once those are known, while the textures are read.
        AssetResult result = AssetPreparer.Prepare(options, output, use => shaderTask = shaders is null ? null : Task.Run(() => PrepareShaders(args[1], shaders, threads, args.Contains("--all-programs") ? null : use)));
        TimeSpan assets = clock.Elapsed;
        Console.WriteLine($"{result.ModelFiles} model files ({result.Skipped} skipped, {result.Failures.Count} failed): {result.Models} models, {result.Materials} materials, {result.Textures} textures");
        Console.WriteLine($"{result.BlobsAdded} blobs offered, {result.BlobsStored} stored; {result.PackBytes / 1048576.0:F0} MB in {assets.TotalSeconds:F1} s");
        foreach ((string file, string reason) in result.Failures.Take(10)) Console.WriteLine($"  {file}: {reason}");
        if (result.MissingTextures.Count > 0) Console.WriteLine($"{result.MissingTextures.Count} sampled textures have no file, e.g. {string.Join(", ", result.MissingTextures.Take(3))}");
        if (shaderTask is not null) Console.WriteLine($"{shaderTask.Result}; everything done in {clock.Elapsed.TotalSeconds:F1} s");
        return 0;
    }

    internal static int Inspect(string[] args)
    {
        using AssetPack pack = AssetPack.Open(args[1]);
        if (args.Length < 3)
        {
            foreach (IGrouping<PackEntryKind, PackEntry> kind in pack.Entries.GroupBy(e => e.Kind)) Console.WriteLine($"{kind.Count()} {kind.Key} entries");
            Console.WriteLine($"{pack.BlobCount} blobs, {pack.Length / 1048576.0:F0} MB");
            return 0;
        }

        ModelRecord? model = pack.Model(args[2]);
        if (model is null) return 1;
        Console.WriteLine($"{model.Name}: {model.Shapes.Count} shapes, {model.Materials.Count} materials, {pack.Skeleton(model.Skeleton).Bones.Count} bones");
        foreach (MaterialRef reference in model.Materials)
        {
            MaterialRecord material = pack.Material(reference.Material);
            Console.WriteLine($"  {reference.Name}: {material.ShaderArchive}/{material.ShadingModel} program {material.Program}, textures {string.Join(", ", material.Samplers.Select(s => s.Texture).Where(n => n != ""))}");
        }

        return 0;
    }

    // Prepares each BFSHA archive of the romfs's Shader folder, one after another, into a prepared file in the output folder.
    private static string PrepareShaders(string romfs, string folder, int threads, ProgramUse? use)
    {
        Directory.CreateDirectory(folder);
        Stopwatch clock = Stopwatch.StartNew();
        int programs = 0;
        using RomfsFiles files = new(romfs);
        foreach (string path in files.ShaderFiles())
        {
            BfshaFile archive = BfshaFile.Read(files.Read(path));
            using FileStream output = File.Create(Path.Combine(folder, archive.Name + ".bfsp"));
            programs += ArchivePreparer.Prepare(archive, output, 5, threads, null, use?.Of(archive.Name) ?? (use is null ? null : new Dictionary<string, IReadOnlySet<int>>())).Programs;
        }

        return $"{programs} shader programs in {clock.Elapsed.TotalSeconds:F1} s";
    }

    private static string[] Values(string[] args, string name) =>
        [.. args.Select((value, at) => (value, at)).Where(p => p.value == name && p.at + 1 < args.Length).Select(p => args[p.at + 1])];
}
