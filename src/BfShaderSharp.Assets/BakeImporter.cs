using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using BfShaderSharp.Pack;
using BymlSharp;
using SarcSharp;

namespace BfShaderSharp.Assets;

/// <summary>Reads the per-tile baked lighting of Bake/Scene: an index from each placement hash to the tile its bake is in, and for chosen tiles the atlas textures and per-actor material regions.</summary>
/// <remarks>
/// The index is stored as game data <c>Bake/index</c> in the layout the renderer reads: "KRSB", version 1, the tile names, then the sorted placement hashes with their tile numbers. A tile is stored as
/// <c>Bake/tile/&lt;name&gt;</c>, a record of the textures that exist (each imported as a texture entry under its own name) and, by placement hash, the actor's model, its original material count and its
/// materials' regions of the atlas. A tile that cannot be read is skipped and reported.
/// </remarks>
public static class BakeImporter
{
    public const string IndexEntry = "Bake/index";
    private const string TileSuffix = ".bkres.zs";
    private const string IndexMagic = "KRSB";
    private const int IndexVersion = 1;

    public static string TileEntry(string tile) => $"Bake/tile/{tile}";

    /// <summary>Stores the index of every tile and the data of the tiles named; returns the number of placements indexed and the tiles that could not be read.</summary>
    public static (int Placements, List<string> Skipped) Import(AssetContext context, IReadOnlyCollection<string> tiles, int parallelism)
    {
        string[] files = [.. context.Romfs.BakeTiles()];
        if (files.Length == 0) return (0, []);
        string[] names = [.. files.Select(TileName)];
        ConcurrentBag<(ulong Hash, int Tile)> entries = [];
        ConcurrentBag<string> skipped = [];
        Parallel.For(0, files.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism) }, i =>
        {
            try
            {
                foreach (Byml model in Models(Read(context.Romfs, files[i])))
                    if (ParseGuid(model.AsMap["Guid"].String) is { } hash) entries.Add((hash, i));
            }
            catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or InvalidOperationException or IOException)
            {
                skipped.Add($"{names[i]}: {ex.Message}");
            }
        });

        (ulong Hash, int Tile)[] sorted = [.. entries.GroupBy(e => e.Hash).Select(g => g.First()).OrderBy(e => e.Hash)];
        context.Pack.AddEntry(PackEntryKind.GameData, IndexEntry, context.Pack.Add(WriteIndex(names, sorted)));
        foreach (string tile in tiles) ImportTile(context, files[Array.IndexOf(names, tile)], tile);
        return (sorted.Length, [.. skipped.Order(StringComparer.Ordinal)]);
    }

    private static void ImportTile(AssetContext context, string file, string tile)
    {
        Byml root = Read(context.Romfs, file);
        List<string> textures = [];
        Dictionary<string, int> indices = new(StringComparer.Ordinal);
        SortedDictionary<ulong, Actor> actors = [];
        foreach (Byml element in root.AsMap["DataElements"].AsArray)
        {
            IDictionary<string, Byml> map = element.AsMap;
            List<string> own = map.TryGetValue("TextureNames", out Byml? list) ? [.. list.AsArray.Select(n => n.String)] : [];
            int[] local = new int[own.Count];
            for (int i = 0; i < own.Count; i++)
            {
                if (!indices.TryGetValue(own[i], out int at))
                {
                    at = -1;
                    if (context.Romfs.TextureFile(own[i]) is { } path)
                    {
                        TextureImporter.Import(context, path);
                        at = textures.Count;
                        textures.Add(own[i]);
                    }

                    indices[own[i]] = at;
                }

                local[i] = at;
            }

            if (!map.TryGetValue("ModelElements", out Byml? models)) continue;
            foreach (Byml model in models.AsArray)
                if (ParseGuid(model.AsMap["Guid"].String) is { } hash && !actors.ContainsKey(hash)) actors[hash] = ReadActor(model.AsMap, local);
        }

        RecordWriter writer = new();
        writer.List(textures, (w, name) => w.String(name));
        writer.List(actors.ToList(), (w, pair) =>
        {
            w.U32((uint)(pair.Key >> 32));
            w.U32((uint)pair.Key);
            w.String(pair.Value.Model);
            w.I32(pair.Value.Count);
            w.List(pair.Value.Materials, (m, material) =>
            {
                m.String(material.Name);
                m.I32(material.Index);
                m.I32(material.Texture);
                foreach (float f in material.St) m.F32(f);
            });
        });
        context.Pack.AddEntry(PackEntryKind.GameData, TileEntry(tile), context.Pack.Add(writer.ToArray()));
    }

    private sealed record Material(string Name, int Index, int Texture, float[] St);

    private sealed record Actor(string Model, int Count, List<Material> Materials);

    private static Actor ReadActor(IDictionary<string, Byml> model, int[] local)
    {
        List<Material> materials = [];
        if (model.TryGetValue("MaterialElements", out Byml? elements))
        {
            foreach (Byml material in elements.AsArray)
            {
                IDictionary<string, Byml> e = material.AsMap;
                int texture = e.TryGetValue("TextureIndex", out Byml? index) ? (int)Num(index) : 0;
                IDictionary<string, Byml> scale = e["TexcoordScale"].AsMap, offset = e["TexcoordOffset"].AsMap;
                materials.Add(new Material(e["MaterialName"].String, (int)Num(e["OriginalMaterialIndex"]), texture >= 0 && texture < local.Length ? local[texture] : -1,
                    [Num(scale["X"]), Num(scale["Y"]), Num(offset["X"]), Num(offset["Y"])]));
            }
        }

        int count = model.TryGetValue("OriginalMaterialCount", out Byml? original) ? (int)Num(original) : materials.Count;
        return new Actor(model.TryGetValue("ModelName", out Byml? name) ? name.String : "", count, materials);
    }

    private static float Num(Byml b) => b.Type switch
    {
        BymlType.Float => b.Float,
        BymlType.Double => (float)b.Double,
        BymlType.Int => b.Int,
        BymlType.UInt => b.UInt,
        BymlType.Int64 => b.Int64,
        BymlType.UInt64 => b.UInt64,
        _ => 0f,
    };

    private static Byml Read(RomfsFiles romfs, string path) => Byml.FromBinary(SarcFile.FromBinary(romfs.Read(path))["bkdat.byaml"]!.Data);

    private static IEnumerable<Byml> Models(Byml root)
    {
        foreach (Byml element in root.AsMap["DataElements"].AsArray)
            if (element.AsMap.TryGetValue("ModelElements", out Byml? models))
                foreach (Byml model in models.AsArray) yield return model;
    }

    private static ulong? ParseGuid(string guid)
    {
        int cut = guid.IndexOf('_');
        return ulong.TryParse(cut < 0 ? guid : guid[..cut], NumberStyles.None, CultureInfo.InvariantCulture, out ulong hash) ? hash : null;
    }

    private static string TileName(string path) => Path.GetFileName(path)[..^TileSuffix.Length];

    private static byte[] WriteIndex(string[] tiles, (ulong Hash, int Tile)[] sorted)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, true))
        {
            writer.Write(Encoding.ASCII.GetBytes(IndexMagic));
            writer.Write(IndexVersion);
            writer.Write(tiles.Length);
            foreach (string tile in tiles) writer.Write(tile);
            writer.Write(sorted.Length);
            foreach ((ulong hash, int tile) in sorted)
            {
                writer.Write(hash);
                writer.Write(tile);
            }
        }

        return stream.ToArray();
    }
}
