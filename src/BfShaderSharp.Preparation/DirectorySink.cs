namespace BfShaderSharp.Preparation;

/// <summary>An <see cref="IExportSink"/> that writes into a folder, creating subfolders as needed.</summary>
/// <remarks>Each file is written to a temporary name and moved into place, so a reader never sees a half-written file and an interrupted run leaves none.</remarks>
public sealed class DirectorySink(string root) : IExportSink
{
    public bool Exists(string path) => File.Exists(Resolve(path));

    public void WriteText(string path, string text)
    {
        string target = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = $"{target}.{Environment.CurrentManagedThreadId}.tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, target, overwrite: true);
    }

    private string Resolve(string path) => Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
}
