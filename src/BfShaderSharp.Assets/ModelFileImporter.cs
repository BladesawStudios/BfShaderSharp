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
    private int _animations;
    private readonly List<(string File, string Reason)> _failures = [];

    public int Models => _models;

    public int Materials => _materials;

    /// <summary>The name a model file's entries start with: its stem, and for a file whose second name differs from the stem (Npc_Zelda.Npc_Zelda_Black.bfres, a variant of the first) the stem and that name, so the variants of one stem never share an entry name.</summary>
    public static string EntryName(string stem, string fileName)
    {
        string[] parts = fileName.Split('.');
        return parts.Length > 2 && parts[0] == stem && parts[1].Length > 0 && parts[1] != stem && !parts[1].StartsWith("bfres", StringComparison.OrdinalIgnoreCase) && !parts[1].StartsWith("sbfres", StringComparison.OrdinalIgnoreCase) ? $"{stem}/{parts[1]}" : stem;
    }

    public int Skipped => _skipped;

    public int Animations => _animations;

    public IReadOnlyList<(string File, string Reason)> Failures
    {
        get
        {
            lock (_failures) return [.. _failures];
        }
    }

    public void Import(string path) => Import(RomfsFiles.StemOf(path), Path.GetFileName(path), () => context.Romfs.Read(path));

    /// <summary>Imports a model file given as bytes under a name; the file name is used in failure reports.</summary>
    public void Import(string stem, string fileName, Func<byte[]> read)
    {
        try
        {
            byte[] bytes = read();
            if (BfresFile.IsPackage(bytes))
            {
                Interlocked.Increment(ref _skipped);
                return;
            }

            ResFile file = BfresFile.Read(bytes);
            Interlocked.Add(ref _animations, AnimationImporter.Import(context, stem, file));
            foreach (ResModel model in file.Models.Values)
            {
                BlobKey key = ModelImporter.Import(context, model);
                string name = EntryName(stem, fileName);
                context.Pack.AddEntry(PackEntryKind.Model, file.Models.Count == 1 ? name : $"{name}/{model.Name}", key);
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
            lock (_failures) _failures.Add((fileName, exception.Message));
        }
    }
}
