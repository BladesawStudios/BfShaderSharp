namespace BfShaderSharp.Pack;

/// <summary>A named thing a pack holds: its kind, its name and the key of the record that describes it.</summary>
/// <remarks>Entries are listed by kind and then by name, so a pack lists them in the same order whoever wrote it.</remarks>
public readonly record struct PackEntry(PackEntryKind Kind, string Name, BlobKey Key);
