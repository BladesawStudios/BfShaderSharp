using System.Collections.Concurrent;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Decompile;
using BfShaderSharp.Preparation;
using BfShaderSharp.Tests.Corpus;

namespace BfShaderSharp.Tests;

public class PreparationCorpusTests
{
    private sealed class MemorySink : IExportSink
    {
        public ConcurrentDictionary<string, string> Files { get; } = new();

        public bool Exists(string path) => Files.ContainsKey(path);

        public void WriteText(string path, string text) => Files[path] = text;
    }

    [CorpusFact]
    public void ExportsTheProgramsOfASmallModel()
    {
        if (!AmpereLowering.IsAvailable) return;
        foreach (string folder in GameFiles.Folders)
        foreach ((string name, byte[] data) in GameFiles.BfshaFiles(folder).Where(f => f.Name.StartsWith("Terrain")))
        {
            ShaderModel model = BfshaFile.Read(data).Model(0);
            MemorySink sink = new();
            int exported = new ModelExporter(sink).Export(model, program => program < 4);
            Assert.Equal(4, exported);
            Assert.Contains($"{model.Name}/0/report.json", sink.Files.Keys);
            Assert.Contains(sink.Files.Keys, key => key.EndsWith(".glsl"));
            Assert.Equal(0, new ModelExporter(sink).Export(model, program => program < 4));
        }
    }
}
