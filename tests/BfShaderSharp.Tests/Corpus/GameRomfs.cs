namespace BfShaderSharp.Tests.Corpus;

/// <summary>Finds the game romfs folders the Switch corpus tests run against, named by BFS_ROMFS and separated by semicolons.</summary>
/// <remarks>A game is recognised by what it keeps: Breath of the Wild has Pack/Bootup_Graphics.pack, Tears of the Kingdom a TexToGo folder and a Shader folder.</remarks>
internal static class GameRomfs
{
    internal const string Env = "BFS_ROMFS";

    internal static IReadOnlyList<string> Folders =>
        (Environment.GetEnvironmentVariable(Env) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Where(Directory.Exists).ToList();

    internal static string? Botw => Folders.FirstOrDefault(f => File.Exists(Path.Combine(f, "Pack", "Bootup_Graphics.pack")));

    internal static string? Totk => Folders.FirstOrDefault(f => Directory.Exists(Path.Combine(f, "TexToGo")) && Directory.Exists(Path.Combine(f, "Shader")));
}

/// <summary>A fact that runs only when BFS_ROMFS names a Breath of the Wild romfs.</summary>
public sealed class BotwFactAttribute : FactAttribute
{
    public BotwFactAttribute()
    {
        if (GameRomfs.Botw is null) Skip = $"Set {GameRomfs.Env} to a Breath of the Wild romfs to run this test.";
    }
}

/// <summary>A fact that runs only when BFS_ROMFS names a Tears of the Kingdom romfs.</summary>
public sealed class TotkFactAttribute : FactAttribute
{
    public TotkFactAttribute()
    {
        if (GameRomfs.Totk is null) Skip = $"Set {GameRomfs.Env} to a Tears of the Kingdom romfs to run this test.";
    }
}
