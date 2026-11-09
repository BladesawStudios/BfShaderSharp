using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using BfShaderSharp.Bnsh;
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
internal sealed class StageCompiler(BlobPool blobs)
{
    private readonly ConcurrentDictionary<string, Lazy<(int Blob, string? Error)>> _stages = new();
    private int _decompiled;

    internal int Decompiled => _decompiled;

    // The blob index of the stage's GLSL, or BlobPool.None with the reason when it cannot be decompiled.
    internal (int Blob, string? Error) Compile(ShaderStageCode code)
    {
        string key = $"{(int)code.Stage}:{Convert.ToBase64String(SHA256.HashData(code.ByteCode.Span))}";
        return _stages.GetOrAdd(key, _ => new Lazy<(int, string?)>(() => Translate(code))).Value;
    }

    private (int Blob, string? Error) Translate(ShaderStageCode code)
    {
        Interlocked.Increment(ref _decompiled);
        try
        {
            return (blobs.Add(Encoding.UTF8.GetBytes(ShaderDecompiler.ToGlsl(code))), null);
        }
        catch (BfShaderFormatException ex)
        {
            return (BlobPool.None, ex.Message);
        }
    }
}
