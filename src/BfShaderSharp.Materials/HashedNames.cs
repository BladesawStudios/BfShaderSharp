using System.Globalization;

namespace BfShaderSharp.Materials;

/// <summary>Turns the keys of a Switch 2 material back into names.</summary>
/// <remarks>
/// A file that leaves names out keys its entries by the hash of the name, which BfresSharp shows as 0x followed by
/// 16 hex digits. The names come from the game's external string table, given as a hash-to-name map. A key that does
/// not look like a hash is already a name.
/// </remarks>
internal static class HashedNames
{
    /// <summary>The name a key stands for, or null when it is a hash the table does not know.</summary>
    public static string? Resolve(string key, IReadOnlyDictionary<ulong, string> names)
    {
        if (key.Length != 18 || !key.StartsWith("0x", StringComparison.Ordinal) || !ulong.TryParse(key.AsSpan(2), NumberStyles.HexNumber, null, out ulong hash)) return key;
        return names.GetValueOrDefault(hash);
    }

    /// <summary>The name a key stands for, or the key itself when it is a hash the table does not know.</summary>
    public static string NameOrKey(string key, IReadOnlyDictionary<ulong, string> names) => Resolve(key, names) ?? key;
}
