using BfShaderSharp.Pack;
using BymlSharp;
using SarcSharp;

namespace BfShaderSharp.Assets;

/// <summary>Resolves an actor to the model it draws and the animation archives it uses, and takes its cloth and helper-bone physics files, from its pack under Pack/Actor.</summary>
/// <remarks>
/// An actor's pack is a SARC of BYML files that inherit from each other through <c>$parent</c>. The model name joins ModelProjectName and FmdbName, each taken from the nearest file in the ModelInfo's
/// parent chain that defines it, and the animation archives are listed in the pack's AnimationParam. The actor is stored as game data <c>Actor/&lt;name&gt;</c> (the model name, the archives and the
/// physics file names) and each physics file as <c>Actor/&lt;name&gt;/&lt;file&gt;</c>. The physics comes from the actor's own pack, or failing that the pack named for its model.
/// </remarks>
public static class ActorImporter
{
    private static readonly string[] PhysicsExtensions = [".bphcl", ".bphhb"];

    public sealed record Resolved(string ModelName, IReadOnlyList<string> AnimPackNames);

    public static string EntryName(string actor) => $"Actor/{actor}";

    public static string PhysicsEntryName(string actor, string file) => $"Actor/{actor}/{file}";

    private static string PackPath(string actor) => $"Pack/Actor/{actor}.pack.zs";

    /// <summary>The model and animation archives of an actor, or null when it has no pack or its pack names no real model.</summary>
    public static Resolved? Resolve(RomfsFiles romfs, string actor)
    {
        string path = Path.Combine(romfs.Root, PackPath(actor));
        if (!File.Exists(path)) return null;
        SarcFile sarc = SarcFile.FromBinary(romfs.Read(path));
        List<string> entries = [.. sarc.Entries.Select(e => e.Name)];
        if (ModelInfoKey(sarc, entries, actor) is not { } modelInfoKey) return null;

        // Some "Player*" effect actors carry a ModelInfo of another schema that names no model.
        Byml? project = null, fmdb = null;
        foreach (IDictionary<string, Byml> file in ParentChain(sarc, [.. entries], modelInfoKey))
        {
            if (project is null && file.TryGetValue("ModelProjectName", out Byml? p)) project = p;
            if (fmdb is null && file.TryGetValue("FmdbName", out Byml? f)) fmdb = f;
        }

        return project is null || fmdb is null ? null : new Resolved($"{project.String}.{fmdb.String}", AnimPackNames(sarc, entries));
    }

    /// <summary>The cloth and helper-bone files of an actor by file name, from its own pack or else that of its model; empty when neither has any.</summary>
    public static IReadOnlyList<(string File, byte[] Data)> Physics(RomfsFiles romfs, string actor, string model)
    {
        foreach (string name in new[] { actor, model })
        {
            string path = Path.Combine(romfs.Root, PackPath(name));
            if (!File.Exists(path)) continue;
            try
            {
                List<(string, byte[])> found = [.. SarcFile.FromBinary(romfs.Read(path)).Entries
                    .Where(e => PhysicsExtensions.Any(x => e.Name.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
                    .Select(e => (Path.GetFileName(e.Name), e.Data))];
                if (found.Count > 0) return found;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
            }
        }

        return [];
    }

    /// <summary>Stores what the actor's pack says; returns false when the actor has no pack at all.</summary>
    public static bool Import(AssetContext context, string actor)
    {
        if (!File.Exists(Path.Combine(context.Romfs.Root, PackPath(actor)))) return false;
        Resolved? resolved = Resolve(context.Romfs, actor);
        IReadOnlyList<(string File, byte[] Data)> physics = Physics(context.Romfs, actor, resolved?.ModelName ?? actor);
        RecordWriter writer = new();
        writer.String(resolved?.ModelName ?? "");
        writer.List(resolved?.AnimPackNames ?? [], (w, name) => w.String(name));
        writer.List(physics, (w, file) => w.String(file.File));
        context.Pack.AddEntry(PackEntryKind.GameData, EntryName(actor), context.Pack.Add(writer.ToArray()));
        foreach ((string file, byte[] data) in physics) context.Pack.AddEntry(PackEntryKind.GameData, PhysicsEntryName(actor, file), context.Pack.Add(data));
        return true;
    }

    /// <summary>Reads an actor stored by <see cref="Import"/>: the resolution (null when the actor had no real model) and the names of its physics files.</summary>
    public static (Resolved? Resolved, IReadOnlyList<string> PhysicsFiles)? Read(AssetPack pack, string actor)
    {
        if (pack.GameData(EntryName(actor)) is not { } bytes) return null;
        RecordReader reader = new(bytes);
        string model = reader.String();
        List<string> anims = reader.List(2, r => r.String());
        List<string> physics = reader.List(2, r => r.String());
        return (model.Length == 0 ? null : new Resolved(model, anims), physics);
    }

    // A pack can hold several ModelInfo files (an armour piece's leggings pack carries the helmet's too), so the one to read is the one the actor's ActorParam points at, possibly inherited;
    // failing that, the file named for the actor, then the first.
    private static string? ModelInfoKey(SarcFile sarc, List<string> entries, string actor)
    {
        HashSet<string> names = [.. entries];
        foreach (IDictionary<string, Byml> actorParam in ParentChain(sarc, names, $"Actor/{actor}.engine__actor__ActorParam.bgyml"))
        {
            if (actorParam.TryGetValue("Components", out Byml? components) && components.Type == BymlType.Map
                && components.AsMap.TryGetValue("ModelInfoRef", out Byml? reference) && reference.Type == BymlType.String && reference.String.Length > 0)
            {
                string key = Entry(reference.String);
                if (names.Contains(key)) return key;
                break;
            }
        }

        string named = $"Component/ModelInfo/{actor}.engine__component__ModelInfo.bgyml";
        return names.Contains(named) ? named : entries.FirstOrDefault(k => k.StartsWith("Component/ModelInfo/", StringComparison.Ordinal));
    }

    private static List<string> AnimPackNames(SarcFile sarc, List<string> entries)
    {
        List<string> packs = [];
        string? key = entries.FirstOrDefault(k => k.StartsWith("Component/AnimationParam/", StringComparison.Ordinal));
        if (key is null) return packs;
        IDictionary<string, Byml> animParam = Byml.FromBinary(sarc[key]!.Data).AsMap;
        if (animParam.TryGetValue("AnimationResources", out Byml? resources))
            foreach (Byml entry in resources.AsArray)
                if (entry.AsMap.TryGetValue("ModelProjectName", out Byml? name)) packs.Add(name.String);
        return packs;
    }

    private static List<IDictionary<string, Byml>> ParentChain(SarcFile sarc, HashSet<string> entries, string name)
    {
        List<IDictionary<string, Byml>> chain = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        string? at = name;
        while (at is not null && seen.Add(at) && entries.Contains(at))
        {
            IDictionary<string, Byml> file;
            try
            {
                file = Byml.FromBinary(sarc[at]!.Data).AsMap;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A parent that cannot be parsed ends the chain at the last one that could.
                break;
            }

            chain.Add(file);
            at = file.TryGetValue("$parent", out Byml? parent) && parent.Type == BymlType.String && parent.String.Length > 0 ? Entry(parent.String) : null;
        }

        return chain;
    }

    private static string Entry(string reference)
    {
        string name = reference.TrimStart('?');
        if (name.StartsWith("Work/", StringComparison.Ordinal)) name = name["Work/".Length..];
        return name.EndsWith(".gyml", StringComparison.Ordinal) ? name[..^".gyml".Length] + ".bgyml" : name;
    }
}
