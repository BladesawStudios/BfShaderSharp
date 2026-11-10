using BfShaderSharp.Bnsh;
using BfShaderSharp.Decompile;
using BfShaderSharp.Decompile.Ampere;
using BfShaderSharp.Errors;
using BfShaderSharp.Tests.Decompile.Ampere;
using Ryujinx.Graphics.Shader;
using ShaderStage = BfShaderSharp.Bnsh.ShaderStage;

namespace BfShaderSharp.Tests.Decompile;

/// <summary>Builds the control blocks and headers of compute, task and mesh stages for the tests.</summary>
internal static class StageBytes
{
    public const uint Magic = 0x98761234;

    public static byte[] Control(uint threads = 0, uint limits = 0)
    {
        byte[] control = new byte[0xe0];
        BitConverter.TryWriteBytes(control.AsSpan(0), Magic);
        BitConverter.TryWriteBytes(control.AsSpan(0x58), threads);
        BitConverter.TryWriteBytes(control.AsSpan(0x5c), 1U);
        BitConverter.TryWriteBytes(control.AsSpan(0x60), limits);
        return control;
    }

    public static byte[] Header(byte systemValues = 0, byte generic0 = 0)
    {
        byte[] header = new byte[0x80];
        header[0x35] = systemValues;
        header[0x36] = generic0;
        return header;
    }

    // ISBEWR with the 32-bit, attribute and output flags: stores R255 (zero) at the address in R255 plus the offset.
    public static AmpereInstruction StageBufferWrite(uint offset = 4) => Asm.Insn(0x927, w1: offset << 8, w2: 0xb800);

    public static ShaderStageCode Code(ShaderStage stage, byte[] header, byte[] control, params AmpereInstruction[] instructions) =>
        new(stage, (byte[])[.. header, .. Asm.Program(instructions)], control);
}

public class ComputeControlTests
{
    [Fact]
    public void TheGroupSizeIsTheThreeWordsAt0x58()
    {
        byte[] control = StageBytes.Control(threads: 64);
        BitConverter.TryWriteBytes(control.AsSpan(0x60), 4U);
        Assert.Equal((64, 1, 4), ComputeControl.WorkgroupSize(control));
    }

    [Fact]
    public void ABlockWithoutTheMagicOrTooShortGivesOneThread()
    {
        Assert.Equal((1, 1, 1), ComputeControl.WorkgroupSize(new byte[0xe0]));
        Assert.Equal((1, 1, 1), ComputeControl.WorkgroupSize(new byte[8]));
    }

    [Fact]
    public void ADimensionIsClampedToWhatTheHardwareAllows()
    {
        Assert.Equal((1024, 1, 1), ComputeControl.WorkgroupSize(StageBytes.Control(threads: 5000)));
        Assert.Equal((1, 1, 1), ComputeControl.WorkgroupSize(StageBytes.Control(threads: 0)));
    }
}

public class MeshProgramReaderTests
{
    [Fact]
    public void TheThreadCountIsTheLowByteOfTheWordAt0x58()
    {
        Assert.Equal((32, 1, 1), MeshProgramReader.WorkgroupSize(StageBytes.Control(threads: 0x01000020)));
        Assert.Equal((1, 1, 1), MeshProgramReader.WorkgroupSize(new byte[4]));
    }

    [Fact]
    public void ATaskStageNeedsNoHeaderInformation()
    {
        MeshProgramInfo task = MeshProgramReader.Read(ShaderStage.Task, [], []);
        Assert.Equal(MeshProgramKind.Task, task.Kind);
    }

    [Fact]
    public void AMeshStageTakesItsLimitsAndLinesFromTheControlBlockAndHeader()
    {
        // Position and point size, then generic 0 with x, y and z.
        MeshProgramInfo mesh = MeshProgramReader.Read(ShaderStage.Mesh, StageBytes.Header(systemValues: 0xf8, generic0: 0x07), StageBytes.Control(32, 0x00400080));
        Assert.Equal((64, 128), (mesh.MaxVertices, mesh.MaxPrimitives));
        Assert.Equal(
            [MeshLineKind.PointSize, MeshLineKind.PositionX, MeshLineKind.PositionY, MeshLineKind.PositionZ, MeshLineKind.PositionW, MeshLineKind.Generic, MeshLineKind.Generic, MeshLineKind.Generic],
            mesh.VertexLines.Select(l => l.Kind));
        Assert.Equal([0, 1, 2], mesh.VertexLines.Where(l => l.Kind == MeshLineKind.Generic).Select(l => l.Index));
    }

    [Fact]
    public void AGenericAttributeKeepsItsIndexAndTheOddOnesUseTheHighNibble()
    {
        byte[] header = StageBytes.Header();
        header[0x37] = 0x30;
        MeshProgramInfo mesh = MeshProgramReader.Read(ShaderStage.Mesh, header, StageBytes.Control(32, 0x00100010));
        Assert.Equal([new MeshLine(MeshLineKind.Generic, 12), new MeshLine(MeshLineKind.Generic, 13)], mesh.VertexLines);
    }

    [Fact]
    public void AMeshStageWithoutAControlBlockIsMalformed()
    {
        Assert.Throws<BfShaderFormatException>(() => MeshProgramReader.Read(ShaderStage.Mesh, StageBytes.Header(), new byte[0x70]));
    }

    [Fact]
    public void ClipDistanceOutputsAreNotDecompiled()
    {
        byte[] header = StageBytes.Header();
        header[0x48] = 1;
        Assert.Throws<BfShaderUnsupportedException>(() => MeshProgramReader.Read(ShaderStage.Mesh, header, StageBytes.Control(32, 0x00100010)));
    }
}

public class MeshProgramInfoTests
{
    [Fact]
    public void TheStageBufferRegionsFollowEachOtherInWorkgroupMemory()
    {
        MeshProgramInfo mesh = new() { Kind = MeshProgramKind.Mesh, PayloadWords = 100, MaxVertices = 40, MaxPrimitives = 10, VertexLines = [new MeshLine(MeshLineKind.PositionX), new MeshLine(MeshLineKind.PositionY)] };
        Assert.Equal(100, mesh.AttributeBase);
        Assert.Equal(2 * 2 * 32, mesh.AttributeWords);
        Assert.Equal(228, mesh.MapBase);
        Assert.Equal(4 + 30 + 4, mesh.MapWords);
        Assert.Equal(mesh.MapBase + mesh.MapWords, mesh.PrimitiveCountWord);
        Assert.Equal((mesh.PrimitiveCountWord + 1) * 4, mesh.SharedMemoryBytes);
    }
}

public class StageDecompilationTests
{
    [Fact]
    public void AComputeStageHasNoHeaderAndTakesItsGroupSizeFromTheControlBlock()
    {
        ShaderStageCode code = new(ShaderStage.Compute, Asm.Program(Asm.Exit()), StageBytes.Control(threads: 8));
        string glsl = ShaderDecompiler.ToGlsl(code);
        Assert.Contains("local_size_x = 8, local_size_y = 1, local_size_z = 1", glsl);
        Assert.DoesNotContain("EXT_mesh_shader", glsl);
    }

    [Fact]
    public void ATaskStageBecomesAnEntryPointThatEmitsItsWorkgroups()
    {
        string glsl = ShaderDecompiler.ToGlsl(StageBytes.Code(ShaderStage.Task, StageBytes.Header(), StageBytes.Control(threads: 1), StageBytes.StageBufferWrite(), Asm.Exit()));
        Assert.Contains("#extension GL_EXT_mesh_shader : require", glsl);
        Assert.Contains("taskPayloadSharedEXT uint task_payload[512];", glsl);
        Assert.Contains("void mesh_body()", glsl);
        Assert.Contains("EmitMeshTasksEXT(count, 1u, 1u);", glsl);
    }

    [Fact]
    public void AMeshStageSetsItsOutputsFromTheStageBuffer()
    {
        string glsl = ShaderDecompiler.ToGlsl(StageBytes.Code(ShaderStage.Mesh, StageBytes.Header(systemValues: 0xf0, generic0: 0x03), StageBytes.Control(32, 0x00400080), StageBytes.StageBufferWrite(), Asm.Exit()));
        Assert.Contains("layout (triangles, max_vertices = 64, max_primitives = 128) out;", glsl);
        Assert.Contains("layout (location = 0) out vec4 out_attr0[64];", glsl);
        Assert.Contains("SetMeshOutputsEXT(64u, count);", glsl);
        Assert.Contains("gl_MeshVerticesEXT[v].gl_Position = position;", glsl);
        Assert.Contains("gl_PrimitiveTriangleIndicesEXT[p] = uvec3(", glsl);
    }
}
