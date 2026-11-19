using System.Collections.Concurrent;
using System.Security.Cryptography;
using Utf8Code = Ryujinx.Graphics.Shader.Utf8Code;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Container;
using BfShaderSharp.Decompile;
using BfShaderSharp.Errors;
using BfShaderSharp.Prepared;

namespace BfShaderSharp.Preparation;

/// <summary>Decompiles stages to GLSL and stores the text as blobs, translating identical stage code once.</summary>
/// <remarks>
/// Stage code is identified by a hash of its bytes and stage, so the many variations that share a stage share one
/// translation and one blob. Safe to use from many threads; threads that ask for the same stage wait for the one
/// translating it. A stage that fails is remembered as failed and the reason is reported once.
/// </remarks>
internal sealed class StageCompiler(BlobPool blobs, StageText text = StageText.Raw)
{
    private readonly ConcurrentDictionary<string, Lazy<(int Blob, string? Error)>> _stages = new();
    private int _decompiled;

    internal int Decompiled => _decompiled;

    // The blob index of the stage's GLSL, or BlobPool.None with the reason when it cannot be decompiled. Reflected text depends on the names
    // and slots of the stage's reflection as well as on its code, so the reflection is part of what identifies the stage.
    internal (int Blob, string? Error) Compile(ShaderStageCode code, StageReflection? reflection = null)
    {
        string key = $"{(int)code.Stage}:{Convert.ToBase64String(SHA256.HashData(code.ByteCode.Span))}";
        if (text == StageText.Reflected) key += ":" + Signature(reflection);
        return _stages.GetOrAdd(key, _ => new Lazy<(int, string?)>(() => Translate(code, reflection))).Value;
    }

    private static string Signature(StageReflection? reflection)
    {
        if (reflection is not { } r) return "-";
        System.Text.StringBuilder builder = new();
        void Group(char tag, ResDict names, Func<int, int> slot)
        {
            for (int i = 0; i < names.Count; i++) builder.Append(tag).Append(names.Key(i)).Append('@').Append(slot(i)).Append(';');
        }

        Group('s', r.Samplers, r.SamplerSlot);
        Group('b', r.UniformBuffers, r.UniformBufferSlot);
        Group('i', r.Inputs, r.InputSlot);
        Group('o', r.Outputs, r.OutputSlot);
        return Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private (int Blob, string? Error) Translate(ShaderStageCode code, StageReflection? reflection)
    {
        Interlocked.Increment(ref _decompiled);
        try
        {
            if (text == StageText.Reflected) return (blobs.Add(System.Text.Encoding.UTF8.GetBytes(ReflectedGlsl.Extract(code, reflection))), null);
            using Utf8Code glsl = ShaderDecompiler.ToGlslUtf8(code, debug: true);
            return (blobs.Add(glsl.Span), null);
        }
        catch (BfShaderFormatException ex)
        {
            return (BlobPool.None, ex.Message);
        }
    }
}
