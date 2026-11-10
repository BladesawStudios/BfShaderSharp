using System.Buffers.Binary;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Errors;
using Ryujinx.Graphics.Shader;
using ShaderStage = BfShaderSharp.Bnsh.ShaderStage;

namespace BfShaderSharp.Decompile;

/// <summary>Reads what a task or mesh stage declares about itself: its thread count, output sizes and the attributes its vertices carry.</summary>
/// <remarks>
/// The thread count is the low byte of the control block's word at 0x58. A mesh stage's word at 0x60 holds its vertex limit in
/// the high half and its primitive limit in the low half. The attributes come from the output map of the stage's program
/// header, which has the layout of a vertex program's: system values at 0x35 and one nibble of component flags for each
/// generic attribute from 0x36. A stage buffer line is kept for each enabled output, system values first, in order. The
/// per-primitive outputs of a mesh program are not described by its own header, so none are kept.
/// </remarks>
internal static class MeshProgramReader
{
    private const uint Magic = 0x98761234;
    private const int Threads = 0x58;
    private const int Limits = 0x60;
    private const int SystemValues = 0x35;
    private const int Generics = 0x36;
    private const int ClipAndOther = 0x48;
    private const int HeaderEnd = 0x50;

    public static (int X, int Y, int Z) WorkgroupSize(ReadOnlySpan<byte> control) =>
        control.Length >= Threads + 4 && BinaryPrimitives.ReadUInt32LittleEndian(control) == Magic
            ? ((int)Math.Clamp(BinaryPrimitives.ReadUInt32LittleEndian(control[Threads..]) & 0xff, 1U, 1024U), 1, 1)
            : (1, 1, 1);

    public static MeshProgramInfo Read(ShaderStage stage, ReadOnlySpan<byte> header, ReadOnlySpan<byte> control)
    {
        if (stage == ShaderStage.Task) return new MeshProgramInfo { Kind = MeshProgramKind.Task };
        if (header.Length < HeaderEnd || control.Length < Limits + 4 || BinaryPrimitives.ReadUInt32LittleEndian(control) != Magic)
            throw new BfShaderFormatException("A mesh stage needs its program header and control block", 0);
        if (BinaryPrimitives.ReadUInt16LittleEndian(header[ClipAndOther..]) != 0) throw new BfShaderUnsupportedException("A mesh stage with clip distance or fixed-function outputs is not decompiled");
        uint limits = BinaryPrimitives.ReadUInt32LittleEndian(control[Limits..]);
        return new MeshProgramInfo { Kind = MeshProgramKind.Mesh, MaxVertices = (int)(limits >> 16), MaxPrimitives = (int)(limits & 0xffff), VertexLines = Lines(header) };
    }

    private static MeshLine[] Lines(ReadOnlySpan<byte> header)
    {
        List<MeshLine> lines = [];
        MeshLineKind[] system = [MeshLineKind.Unused, MeshLineKind.Unused, MeshLineKind.Unused, MeshLineKind.PointSize, MeshLineKind.PositionX, MeshLineKind.PositionY, MeshLineKind.PositionZ, MeshLineKind.PositionW];
        for (int bit = 0; bit < 8; bit++)
        {
            if ((header[SystemValues] >> bit & 1) != 0) lines.Add(new MeshLine(system[bit]));
        }

        for (int generic = 0; generic < 32; generic++)
        {
            int flags = header[Generics + (generic >> 1)] >> (generic & 1) * 4;
            for (int element = 0; element < 4; element++)
            {
                if ((flags >> element & 1) != 0) lines.Add(new MeshLine(MeshLineKind.Generic, generic * 4 + element));
            }
        }

        return [.. lines];
    }
}
