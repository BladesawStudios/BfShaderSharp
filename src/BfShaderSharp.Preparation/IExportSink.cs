namespace BfShaderSharp.Preparation;

/// <summary>Where exported files go: a folder, an archive or memory.</summary>
/// <remarks>Paths are relative and use '/'. The exporter holds no other state about where output lands.</remarks>
public interface IExportSink
{
    bool Exists(string path);

    void WriteText(string path, string text);
}
