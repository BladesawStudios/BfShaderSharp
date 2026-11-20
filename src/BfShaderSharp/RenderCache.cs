namespace BfShaderSharp;

/// <summary>The one folder where every Bladesaw renderer and the tools that use them keep what they prepare from a user's game files.</summary>
/// <remarks>
/// It is <c>%LOCALAPPDATA%\BladesawStudios\RenderCache</c> (or what the <c>BLADESAW_RENDER_CACHE</c> environment variable names), with a folder per renderer ("king", "owl", "skybill") inside it, so
/// an app and a renderer used on its own share what was prepared. Everything in it is made again from the game files when it is asked for, so a renderer must treat a missing or half-deleted folder
/// as "prepare it again". A renderer's folder holds a <c>version</c> file; <see cref="Folder"/> empties the folder when the version in it differs, so a cache written by another format is never misread.
/// </remarks>
public static class RenderCache
{
    public const string EnvironmentVariable = "BLADESAW_RENDER_CACHE";

    private const string VersionFile = "version";

    public static string Root
    {
        get
        {
            string? overridden = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrWhiteSpace(overridden)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BladesawStudios", "RenderCache")
                : Path.GetFullPath(overridden);
        }
    }

    /// <summary>The folder of one renderer, made if it is missing and emptied if it was written with another <paramref name="version"/> of that renderer's format.</summary>
    public static string Folder(string renderer, int version = 1)
    {
        string folder = Path.Combine(Root, renderer);
        string marker = Path.Combine(folder, VersionFile), text = version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            if (File.Exists(marker) && File.ReadAllText(marker).Trim() == text) return folder;
            if (Directory.Exists(folder)) DeleteContents(folder);
            Directory.CreateDirectory(folder);
            File.WriteAllText(marker, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return folder;
    }

    /// <summary>The size of everything in the cache, in bytes.</summary>
    public static long Size()
    {
        long total = 0;
        try
        {
            if (!Directory.Exists(Root)) return 0;
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                try { total += new FileInfo(file).Length; }
                catch (IOException) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return total;
    }

    /// <summary>Deletes everything in the cache, whichever renderer made it. Returns how many files could not be deleted because something else has them open.</summary>
    public static int Clear() => Directory.Exists(Root) ? DeleteContents(Root) : 0;

    private static int DeleteContents(string folder)
    {
        int failed = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList())
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed++; }
            }

            foreach (string directory in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
            {
                try { Directory.Delete(directory, false); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return failed;
    }
}
