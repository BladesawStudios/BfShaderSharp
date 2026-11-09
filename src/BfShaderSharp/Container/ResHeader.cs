using System.Text;
using BfShaderSharp.Binary;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Container;

/// <summary>The 0x20-byte header that BFSHA and BNSH files share.</summary>
/// <remarks>
/// Read once from the start of a file. Only little-endian files (Switch and Switch 2) are accepted; a file with
/// the other byte order mark is reported as unsupported. Offsets inside a file are relative to the start of that
/// file, so a BNSH inside a BFSHA is read from a view that begins at the BNSH.
/// </remarks>
public readonly record struct ResHeader(
    string Magic,
    byte VersionMicro,
    byte VersionMinor,
    ushort VersionMajor,
    byte AlignmentShift,
    uint NameOffset,
    ushort BlockOffset,
    uint RelocationTableOffset,
    uint FileSize)
{
    public const int Size = 0x20;

    public static ResHeader Read(ByteView view, string expectedMagic)
    {
        if (view.Length < Size) throw new BfShaderFormatException("File is shorter than its header", 0);
        string magic = Encoding.ASCII.GetString(view.Span[..expectedMagic.Length]);
        if (magic != expectedMagic) throw new BfShaderFormatException($"Expected magic {expectedMagic} but found {magic}", 0);
        if (view.U16(0x0C) != 0xFEFF) throw new BfShaderUnsupportedException("Big-endian files are not supported", 0x0C);
        return new ResHeader(magic, view.U8(8), view.U8(9), view.U16(0x0A), view.U8(0x0E), view.U32(0x10), view.U16(0x16), view.U32(0x18), view.U32(0x1C));
    }

    public Version Version => new(VersionMajor, VersionMinor, VersionMicro);
}
