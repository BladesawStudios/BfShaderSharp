using BfShaderSharp.Binary;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Sharc;

/// <summary>A compiled SHARC file (<c>.sharcb</c>, magic BAHS): the shader programs of a game's AGL library, each with its compiled stages.</summary>
/// <remarks>
/// A file lists compiled stages and programs separately; a program names where its stages start. Reading parses both
/// lists and slices the byte code out of the file without copying it. Files of version 9 and later with the Switch
/// layout are read, which includes the version 11 files of Switch 2 titles.
/// </remarks>
public sealed class SharcbFile
{
    private const uint MinimumAlignment = 2048;
    internal const long NameOffset = 0x150;

    private SharcbFile(string name, int version, IReadOnlyList<SharcProgram> programs, IReadOnlyList<SharcVariation> variations)
    {
        Name = name;
        Version = version;
        Programs = programs;
        Variations = variations;
    }

    public string Name { get; }

    public int Version { get; }

    public IReadOnlyList<SharcProgram> Programs { get; }

    /// <summary>Every compiled stage of the file, in the order programs index them.</summary>
    public IReadOnlyList<SharcVariation> Variations { get; }

    public SharcProgram? Program(string name) => Programs.FirstOrDefault(p => p.Name == name);

    /// <summary>The compiled stage of a program for an option combination.</summary>
    public SharcVariation Variation(SharcProgram program, int variation, SharcStage stage)
    {
        int index = program.BinaryIndex(variation, stage);
        if ((uint)index >= (uint)Variations.Count) throw new BfShaderFormatException($"Program {program.Name} indexes stage {index} of {Variations.Count}");
        return Variations[index];
    }

    public static SharcbFile Read(ReadOnlyMemory<byte> bytes)
    {
        ByteView view = new(bytes);
        if (!view.StartsWith(0, "SHAB"u8) && !view.StartsWith(0, "BAHS"u8)) throw new BfShaderFormatException("Expected magic BAHS", 0);
        int version = view.Count(4);
        if (version < 9) throw new BfShaderUnsupportedException($"SHARC version {version} is not read; only 9 and later are", 4);
        uint alignment = view.U32(0x14);
        if (alignment < MinimumAlignment) throw new BfShaderUnsupportedException("Only the Switch layout of SHARC files is read", 0x14);
        long dataOffset = view.U32(0x1C);
        long table = NameOffset + alignment;
        List<SharcVariation> variations = SharcbReader.ReadVariations(view, table, dataOffset, out long next);
        List<SharcProgram> programs = SharcbReader.ReadPrograms(view, next);
        return new SharcbFile(view.CString(NameOffset), version, programs, variations);
    }
}
