using System.Buffers.Binary;
using System.Text;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Assets;

/// <summary>The table of System/Gfx/UnifyTex.Product.100.byml that maps a texture name a material samples to the texture that stands in for it.</summary>
/// <remarks>
/// The game deduplicated near-identical textures: the files of the duplicates were dropped from Tex, and this table names the
/// one kept for each. It is a BYML (version 7) dictionary whose values are all strings; this reads only that shape.
/// </remarks>
public sealed class UnifyTex
{
    private const string Path = "System/Gfx/UnifyTex.Product.100.byml";

    private readonly Dictionary<string, string> _targets;

    private UnifyTex(Dictionary<string, string> targets) => _targets = targets;

    public static UnifyTex Empty { get; } = new([]);

    public int Count => _targets.Count;

    public IEnumerable<KeyValuePair<string, string>> Entries => _targets;

    /// <summary>The texture that stands in for a name, or null when the name is not in the table.</summary>
    public string? TargetOf(string name) => _targets.GetValueOrDefault(name);

    /// <summary>Reads the romfs's table; empty when the game has none.</summary>
    public static UnifyTex Load(RomfsFiles romfs)
    {
        string? file = new[] { ".zs", "" }.Select(e => System.IO.Path.Combine(romfs.Root, Path + e)).FirstOrDefault(File.Exists);
        return file is null ? Empty : Parse(romfs.Read(file));
    }

    public static UnifyTex Parse(byte[] data)
    {
        if (data.Length < 16 || data[0] != 'Y' || data[1] != 'B') throw new BfShaderFormatException("The UnifyTex table is not a BYML file");
        int Int(int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
        string[] keys = Strings(data, Int(4));
        string[] values = Strings(data, Int(8));
        int root = Int(12);
        if (root == 0) return Empty;
        if (data[root] != 0xC1) throw new BfShaderFormatException("The UnifyTex table does not start with a dictionary");
        int count = data[root + 1] | data[root + 2] << 8 | data[root + 3] << 16;
        Dictionary<string, string> targets = new(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int at = root + 4 + i * 8;
            int key = data[at] | data[at + 1] << 8 | data[at + 2] << 16;
            if (data[at + 3] != 0xA0) continue;
            int value = Int(at + 4);
            if ((uint)key >= (uint)keys.Length || (uint)value >= (uint)values.Length) throw new BfShaderFormatException("A UnifyTex entry names a string that is not in the table");
            targets[keys[key]] = values[value];
        }

        return new UnifyTex(targets);
    }

    // A string table: type 0xC2, a u24 count, then count + 1 offsets from the table's start to each string and the end.
    private static string[] Strings(byte[] data, int table)
    {
        if (table == 0) return [];
        if (data[table] != 0xC2) throw new BfShaderFormatException("A BYML string table has the wrong type");
        int count = data[table + 1] | data[table + 2] << 8 | data[table + 3] << 16;
        string[] strings = new string[count];
        for (int i = 0; i < count; i++)
        {
            int start = table + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(table + 4 + i * 4));
            int end = data.AsSpan(start).IndexOf((byte)0);
            strings[i] = Encoding.UTF8.GetString(data.AsSpan(start, end < 0 ? data.Length - start : end));
        }

        return strings;
    }
}
