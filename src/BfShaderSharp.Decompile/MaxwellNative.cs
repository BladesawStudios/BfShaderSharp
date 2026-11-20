using BfShaderSharp.Bnsh;
using BfShaderSharp.Errors;
using Ryujinx.Graphics.Shader;
using Ryujinx.Graphics.Shader.Translation;

namespace BfShaderSharp.Decompile;

/// <summary>Translates the stage code of a Switch BNSH, which is Maxwell code the translator reads as it is.</summary>
/// <remarks>
/// A Switch stage's byte code is a 0x30-byte prefix, the 0x50-byte program header and the instructions grouped under scheduling words,
/// so the image the translator reads starts after the prefix and no lowering is needed.
/// </remarks>
internal static class MaxwellNative
{
    private const int PrefixSize = 0x30;

    public static Utf8Code Translate(ShaderStageCode code, bool debug)
    {
        ReadOnlySpan<byte> bytes = code.ByteCode.Span;
        if (bytes.Length <= PrefixSize + MaxwellProgram.HeaderSize) throw new BfShaderFormatException("Stage code is shorter than its header", 0);
        bool compute = code.Stage == BfShaderSharp.Bnsh.ShaderStage.Compute;
        TranslationFlags flags = (debug ? TranslationFlags.DebugMode : TranslationFlags.None) | (compute ? TranslationFlags.Compute : TranslationFlags.None);
        var options = new TranslationOptions(TargetLanguage.Glsl, TargetApi.OpenGL, flags);
        var access = new GpuAccessor(bytes[PrefixSize..].ToArray(), ImmediateConstants.Read(code.ControlCode.Span, bytes), ComputeControl.WorkgroupSize(code.ControlCode.Span));
        try
        {
            return Translator.CreateContext(0, access, options).Translate().CodeUtf8!;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new BfShaderUnsupportedException($"Translator failed with {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }
}
