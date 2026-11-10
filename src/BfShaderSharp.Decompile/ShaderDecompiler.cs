using BfShaderSharp.Bnsh;
using BfShaderSharp.Decompile.Ampere;
using BfShaderSharp.Errors;
using MeshProgramInfo = Ryujinx.Graphics.Shader.MeshProgramInfo;
using Utf8Code = Ryujinx.Graphics.Shader.Utf8Code;
using Ryujinx.Graphics.Shader.Translation;

namespace BfShaderSharp.Decompile;

/// <summary>Decompiles the stage code of a Switch 2 BNSH to GLSL.</summary>
/// <remarks>
/// A graphics stage's byte code is a 0x80-byte header followed by Ampere instructions; a compute stage's is instructions alone. The instructions are lowered to
/// Maxwell, wrapped with the header's program header, and translated. Control code and immediate constants are not
/// used yet. The translator is third-party code that throws its own exceptions on programs it cannot handle; they are
/// reported as <see cref="BfShaderUnsupportedException"/>, so a caller sees one exception family. With
/// <c>debug</c> set, the GLSL carries a comment for every source instruction.
/// </remarks>
public static class ShaderDecompiler
{
    private const int NativeHeaderSize = 0x80;

    // A stage with nothing but padding does nothing; it is decompiled as the program that exits at once.
    private static ReadOnlySpan<byte> ExitOnly => [0x4d, 0x79, 0, 0, 0, 0, 0, 0, 0, 0, 0x80, 0x03, 0, 0xea, 0x0f, 0];

    private static bool IsPadding(ReadOnlySpan<byte> instructions)
    {
        if (instructions.Length == 0 || instructions.Length % 16 != 0) return false;
        for (int at = 0; at < instructions.Length; at += 16)
        {
            if ((instructions[at] | (instructions[at + 1] & 0xf) << 8) != 0x918) return false;
        }

        return true;
    }

    public static string ToGlsl(ShaderStageCode code, bool debug = false)
    {
        using Utf8Code glsl = ToGlslUtf8(code, debug);
        return glsl.ToString();
    }

    /// <summary>Decompiles to UTF-8 text in a buffer reused by the next call on the same thread; dispose it when done.</summary>
    public static Utf8Code ToGlslUtf8(ShaderStageCode code, bool debug = false)
    {
        ReadOnlySpan<byte> bytes = code.ByteCode.Span;
        bool compute = code.Stage == ShaderStage.Compute;
        bool meshStage = code.Stage is ShaderStage.Task or ShaderStage.Mesh;
        int headerSize = compute ? 0 : NativeHeaderSize;
        if (bytes.Length <= headerSize) throw new BfShaderFormatException("Stage code is shorter than its header", 0);
        TessellationStage tessellation = code.Stage switch { ShaderStage.TessellationControl => TessellationStage.Control, ShaderStage.TessellationEvaluation => TessellationStage.Evaluation, _ => TessellationStage.None };
        ReadOnlySpan<byte> instructions = IsPadding(bytes[headerSize..]) ? ExitOnly : bytes[headerSize..];
        LoweredProgram lowered = AmpereLowerer.Lower(instructions, LoweringOptions.ForStage(instructions, code.Stage == ShaderStage.Geometry, tessellation));
        if (!lowered.Succeeded) throw new BfShaderUnsupportedException($"Ampere lowering rejected the program: {lowered.Error}", lowered.ErrorOffset);
        // A compute stage has no program header: its code starts at the first byte, and the translator is told so. Task and mesh stages are translated
        // the same way, as compute programs that work on a stage buffer in workgroup memory.
        bool computeStyle = compute || meshStage;
        MeshProgramInfo? mesh = meshStage ? MeshProgramReader.Read(code.Stage, bytes, code.ControlCode.Span) : null;
        var workgroup = meshStage ? MeshProgramReader.WorkgroupSize(code.ControlCode.Span) : ComputeControl.WorkgroupSize(code.ControlCode.Span);
        byte[] image = MaxwellProgram.Build(computeStyle ? new byte[MaxwellProgram.HeaderSize] : bytes, lowered.Instructions);
        TranslationFlags flags = (debug ? TranslationFlags.DebugMode : TranslationFlags.None) | (computeStyle ? TranslationFlags.Compute : TranslationFlags.None);
        var options = new TranslationOptions(TargetLanguage.Glsl, TargetApi.OpenGL, flags);
        try
        {
            return Translator.CreateContext(computeStyle ? (ulong)MaxwellProgram.HeaderSize : 0, new GpuAccessor(image, [], workgroup, mesh), options).Translate().CodeUtf8!;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new BfShaderUnsupportedException($"Translator failed with {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }
}
