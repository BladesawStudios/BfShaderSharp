using BfresSharp;
using BfresSharp.Errors;
using BfresSharp.Model;
using BfShaderSharp.Pack;

namespace BfShaderSharp.Assets;

/// <summary>Imports every model of one model file and names each in the pack.</summary>
/// <remarks>
/// A model is named by its file's name before the first dot when the file holds one model, and by that name, a slash and
/// the model's own name when it holds several. A package file (MCPK) or one BfresSharp does not support is reported as
/// skipped; a file that is malformed is reported as a failure. Neither stops the others.
/// </remarks>
public sealed class ModelFileImporter(AssetContext context)
{
    private int _models;
    private int _materials;
    private int _skipped;
    private readonly List<(string File, string Reason)> _failures = [];

    public int Models => _models;

    public int Materials => _materials;

    public int Skipped => _skipped;

    public IReadOnlyList<(string File, string Reason)> Failures
    {
        get
        {
            lock (_failures) return [.. _failures];
        }
    }

    public void Import(string path)
    {
        try
        {
            byte[] bytes = context.Romfs.Read(path);
            if (BfresFile.IsPackage(bytes))
            {
                Interlocked.Increment(ref _skipped);
                return;
            }

            ResFile file = BfresFile.Read(bytes);
            string stem = RomfsFiles.StemOf(path);
            foreach (ResModel model in file.Models.Values)
            {
                BlobKey key = ModelImporter.Import(context, model);
                context.Pack.AddEntry(PackEntryKind.Model, file.Models.Count == 1 ? stem : $"{stem}/{model.Name}", key);
                Interlocked.Increment(ref _models);
                Interlocked.Add(ref _materials, model.Materials.Count);
            }
        }
        catch (BfresUnsupportedException)
        {
            Interlocked.Increment(ref _skipped);
        }
        catch (BfresFormatException exception)
        {
            lock (_failures) _failures.Add((Path.GetFileName(path), exception.Message));
        }
    }
}
