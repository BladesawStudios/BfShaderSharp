namespace BfShaderSharp.Pack;

/// <summary>What a named entry of a pack points at.</summary>
/// <remarks>A model entry names a model record, a texture entry a texture record, and a shader source entry the hash of the decompressed shader archive the program numbers of the pack's materials refer to.</remarks>
public enum PackEntryKind : byte
{
    Model = 0,
    Texture = 1,
    ShaderSource = 2,
}
