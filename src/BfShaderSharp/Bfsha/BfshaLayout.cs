using BfShaderSharp.Errors;

namespace BfShaderSharp.Bfsha;

/// <summary>Where a version of the BFSHA format keeps the fields of a program: the offsets of its record and of its binding tables.</summary>
/// <remarks>An offset of -1 means the version has no such field. A location is a pair of 32-bit slots in version 9 (vertex, fragment) and four in version 3 (vertex, geometry, fragment, compute).</remarks>
internal sealed record ProgramLayout(int Size, int Samplers, int Images, int Blocks, int Variation, int UsedAttributes, int Flags, int SamplerCount, int ImageCount, int BlockCount, int LocationSize, int FragmentSlot);

/// <summary>Where a version of the BFSHA format keeps the fields of an option.</summary>
internal sealed record OptionLayout(bool WideCounts, int DefaultChoice, int KeyOffset, int KeyIndex, int Shift);

/// <summary>The layout of one version of the BFSHA format: where the archive, its shading models, options and programs keep their fields.</summary>
/// <remarks>
/// A version is one layout class, chosen once when the archive is opened; no reader tests the version itself. Version 9 is the
/// Switch 2 layout and Tears of the Kingdom's; version 3 is Breath of the Wild's on Switch.
/// </remarks>
internal sealed record BfshaLayout(
    int ModelCount,
    int ModelSize,
    int Images,
    int Blocks,
    int Programs,
    int Keys,
    int Bnsh,
    int DefaultProgram,
    int StaticOptionCount,
    int DynamicOptionCount,
    int ProgramCount,
    int StaticKeyLength,
    int DynamicKeyLength,
    OptionLayout Option,
    ProgramLayout Program)
{
    public static readonly BfshaLayout V9 = new(0x78, 0x100, 0x48, 0x58, 0x88, 0x90, 0xA8, 0xE0, 0xE4, 0xE6, 0xE8, 0xEA, 0xEB,
        new OptionLayout(true, 0x1A, 0x1F, 0x24, 0x25),
        new ProgramLayout(0x40, 0x00, 0x08, 0x10, 0x20, 0x30, 0x34, 0x36, 0x38, 0x3A, 8, 1));

    public static readonly BfshaLayout V3 = new(0x70, 0xC0, -1, 0x48, 0x60, 0x68, 0x80, 0xA4, 0xA8, 0xAA, 0xAC, 0xB0, 0xB1,
        new OptionLayout(false, 0x19, 0x1D, 0x1E, 0x1F),
        new ProgramLayout(0x30, 0x00, -1, 0x08, 0x10, 0x20, 0x24, 0x26, -1, 0x28, 16, 2));

    public static BfshaLayout For(int major) => major switch
    {
        9 => V9,
        3 => V3,
        _ => throw new BfShaderUnsupportedException($"BFSHA version {major} is not supported; versions 3 and 9 are read", 8),
    };
}
