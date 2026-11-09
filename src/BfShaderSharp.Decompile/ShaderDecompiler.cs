using BfShaderSharp.Bnsh;
using BfShaderSharp.Errors;
using Ryujinx.Graphics.Shader.Translation;

namespace BfShaderSharp.Decompile;

/// <summary>Decompiles the stage code of a Switch 2 BNSH to GLSL.</summary>
/// <remarks>
/// A stage's byte code is a 0x80-byte header followed by Ampere instructions. The instructions are lowered to
/// Maxwell, wrapped with the header's program header, and translated. Control code and immediate constants are not
/// used yet.
/// </remarks>
public static class ShaderDecompiler
{
    private const int NativeHeaderSize = 0x80;

    public static string ToGlsl(ShaderStageCode code)
    {
        ReadOnlySpan<byte> bytes = code.ByteCode.Span;
        if (bytes.Length <= NativeHeaderSize) throw new BfShaderFormatException("Stage code is shorter than its header", 0);
        int tessellation = code.Stage switch { ShaderStage.TessellationControl => 1, ShaderStage.TessellationEvaluation => 2, _ => 0 };
        ulong[] lowered = AmpereLowering.Lower(bytes[NativeHeaderSize..], code.Stage == ShaderStage.Geometry, tessellation);
        byte[] image = MaxwellProgram.Build(bytes, lowered);
        var options = new TranslationOptions(TargetLanguage.Glsl, TargetApi.OpenGL, TranslationFlags.DebugMode);
        return Translator.CreateContext(0, new GpuAccessor(image, []), options).Translate().Code;
    }
}
