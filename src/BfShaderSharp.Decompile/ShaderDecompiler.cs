using BfShaderSharp.Bnsh;
using BfShaderSharp.Errors;
using Ryujinx.Graphics.Shader.Translation;

namespace BfShaderSharp.Decompile;

/// <summary>Decompiles the stage code of a Switch 2 BNSH to GLSL.</summary>
/// <remarks>
/// A stage's byte code is a 0x80-byte header followed by Ampere instructions. The instructions are lowered to
/// Maxwell, wrapped with the header's program header, and translated. Control code and immediate constants are not
/// used yet. The translator is third-party code that throws its own exceptions on programs it cannot handle; they are
/// reported as <see cref="BfShaderUnsupportedException"/>, so a caller sees one exception family. With
/// <c>debug</c> set, the GLSL carries a comment for every source instruction.
/// </remarks>
public static class ShaderDecompiler
{
    private const int NativeHeaderSize = 0x80;

    public static string ToGlsl(ShaderStageCode code, bool debug = false)
    {
        ReadOnlySpan<byte> bytes = code.ByteCode.Span;
        if (bytes.Length <= NativeHeaderSize) throw new BfShaderFormatException("Stage code is shorter than its header", 0);
        int tessellation = code.Stage switch { ShaderStage.TessellationControl => 1, ShaderStage.TessellationEvaluation => 2, _ => 0 };
        ulong[] lowered = AmpereLowering.Lower(bytes[NativeHeaderSize..], code.Stage == ShaderStage.Geometry, tessellation);
        byte[] image = MaxwellProgram.Build(bytes, lowered);
        var options = new TranslationOptions(TargetLanguage.Glsl, TargetApi.OpenGL, debug ? TranslationFlags.DebugMode : TranslationFlags.None);
        try
        {
            return Translator.CreateContext(0, new GpuAccessor(image, []), options).Translate().Code;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new BfShaderUnsupportedException($"Translator failed with {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }
}
