using System.Text;
using BfShaderSharp.Binary;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Sharc;

/// <summary>Reads the variation list and program list of a SHARC file.</summary>
/// <remarks>
/// Both lists are sections that start with their size, so a list is followed by the next at its start plus its size.
/// Symbol names are offsets into the string table that begins at <c>0x150</c>. The size of a stage's byte code is the
/// program size in its control block.
/// </remarks>
internal static class SharcbReader
{
    private const uint ControlMagic = 0x98761234;
    private const int ControlProgramSize = 0x38;

    // Reads the variation list that starts at the offset; next is the offset of the section after it.
    internal static List<SharcVariation> ReadVariations(ByteView view, long offset, long dataOffset, out long next)
    {
        next = offset + view.U32(offset);
        int count = view.Count(offset + 4);
        List<SharcVariation> variations = [];
        long at = offset + 8;
        for (int i = 0; i < count; i++)
        {
            variations.Add(ReadVariation(view, at, dataOffset));
            at += view.U32(at);
        }

        return variations;
    }

    private static SharcVariation ReadVariation(ByteView view, long start, long dataOffset)
    {
        uint type = view.U32(start + 4);
        if (type > (uint)SharcStage.Five) throw new BfShaderFormatException($"Unknown stage kind {type}", start + 4);
        long pos = start + 16;
        long byteCode = dataOffset + view.I32(pos);
        ReadOnlyMemory<byte> control = view.Bytes(pos + view.I32(pos + 12), view.Count(pos + 8));
        ByteView controlView = new(control);
        if (control.Length < ControlProgramSize + 4 || controlView.U32(0) != ControlMagic) throw new BfShaderFormatException("Stage control code is not an NVN control block", pos + 12);
        return new SharcVariation((SharcStage)type, view.Bytes(byteCode, controlView.U32(ControlProgramSize)), control,
            Blocks(view, pos + 20, pos), Symbols(view, pos + 32, pos), Symbols(view, pos + 44, pos), Symbols(view, pos + 56, pos), Symbols(view, pos + 68, pos));
    }

    // A list is a count followed by the offset of its first entry from the start of the variation body.
    private static List<SharcBlock> Blocks(ByteView view, long at, long pos)
    {
        int count = view.Count(at);
        long list = pos + view.I32(at + 4);
        List<SharcBlock> blocks = [];
        for (int i = 0; i < count; i++) blocks.Add(new SharcBlock(Name(view, list + i * 16L), view.I32(list + i * 16L + 8), view.I32(list + i * 16L + 12)));
        return blocks;
    }

    private static List<SharcSymbol> Symbols(ByteView view, long at, long pos)
    {
        int count = view.Count(at);
        long list = pos + view.I32(at + 4);
        List<SharcSymbol> symbols = [];
        for (int i = 0; i < count; i++) symbols.Add(new SharcSymbol(Name(view, list + i * 12L), view.I32(list + i * 12L + 8)));
        return symbols;
    }

    private static string Name(ByteView view, long at) => view.CString(SharcbFile.NameOffset + view.U32(at));

    internal static List<SharcProgram> ReadPrograms(ByteView view, long offset)
    {
        int count = view.Count(offset + 4);
        List<SharcProgram> programs = [];
        long at = offset + 8;
        for (int i = 0; i < count; i++)
        {
            programs.Add(ReadProgram(view, at));
            at += view.U32(at);
        }

        return programs;
    }

    private static SharcProgram ReadProgram(ByteView view, long start)
    {
        int nameLength = view.Count(start + 4);
        return new SharcProgram(FixedString(view, start + 16, nameLength), view.I32(start + 8), view.I32(start + 12), ReadMacros(view, start + 16 + nameLength));
    }

    private static string FixedString(ByteView view, long at, int length) => Encoding.UTF8.GetString(view.Bytes(at, length).Span).TrimEnd((char)0);

    private static List<SharcMacro> ReadMacros(ByteView view, long list)
    {
        int count = view.Count(list + 4);
        List<SharcMacro> macros = [];
        long at = list + 8;
        for (int i = 0; i < count; i++)
        {
            int nameLength = view.Count(at + 4);
            int valueCount = view.Count(at + 8);
            long cursor = at + 16 + nameLength;
            List<string> values = [];
            for (int v = 0; v < valueCount; v++)
            {
                string value = view.CString(cursor);
                values.Add(value);
                cursor += Encoding.UTF8.GetByteCount(value) + 1;
            }

            macros.Add(new SharcMacro(FixedString(view, at + 16, nameLength), values));
            at += view.U32(at);
        }

        return macros;
    }
}
