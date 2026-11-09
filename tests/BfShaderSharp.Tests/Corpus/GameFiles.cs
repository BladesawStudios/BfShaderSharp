using ZsDicSharp;

namespace BfShaderSharp.Tests.Corpus;

/// <summary>Finds and decompresses the BFSHA files of the shader folders named for the corpus tests.</summary>
/// <remarks>
/// BFSHA_CORPUS holds Shader directories separated by semicolons. Unset or missing folders yield no files and the
/// corpus facts skip themselves. Files come back decompressed with the dictionaries of the nearest ancestor romfs
/// that has them. BFSHA_CORPUS_STRIDE=n keeps every n-th file, for quick runs.
/// </remarks>
internal static class GameFiles
{
    internal const string Env = "BFSHA_CORPUS";

    internal static int Stride => int.TryParse(Environment.GetEnvironmentVariable("BFSHA_CORPUS_STRIDE"), out int n) && n > 1 ? n : 1;

    internal static IReadOnlyList<string> Folders =>
        (Environment.GetEnvironmentVariable(Env) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Where(Directory.Exists).ToList();

    internal static IEnumerable<(string Name, byte[] Data)> BfshaFiles(string folder, int? stride = null)
    {
        using ZsDic? zs = FindRomfs(folder) is { } romfs ? ZsDic.FromRomfs(romfs) : null;
        int step = stride ?? Stride;
        foreach (string path in Directory.EnumerateFiles(folder, "*.bfsha*", SearchOption.AllDirectories).Where((_, index) => index % step == 0))
        {
            byte[] data = File.ReadAllBytes(path);
            if (ZsDic.IsCompressed(data)) data = zs?.Decompress(data, path) ?? data;
            yield return (Path.GetRelativePath(folder, path), data);
        }
    }

    private static string? FindRomfs(string folder)
    {
        for (DirectoryInfo? dir = new(folder); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Pack", "ZsDic.pack.zs"))) return dir.FullName;
        }

        return null;
    }
}
