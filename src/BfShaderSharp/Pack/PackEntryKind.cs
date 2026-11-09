namespace BfShaderSharp.Pack;

/// <summary>What a named entry of a pack points at.</summary>
/// <remarks>A model entry names a model record, a texture entry a texture record, and a shader source entry the hash of the decompressed shader archive the program numbers of the pack's materials refer to.
/// An animation entry is named by its file's name before the first dot, a slash and the animation's own name; the record binds to a model by bone, material or shape name.</remarks>
public enum PackEntryKind : byte
{
    Model = 0,
    Texture = 1,
    ShaderSource = 2,
    SkeletalAnim = 3,
    MaterialAnim = 4,
    VisibilityAnim = 5,
    ShapeAnim = 6,
}
