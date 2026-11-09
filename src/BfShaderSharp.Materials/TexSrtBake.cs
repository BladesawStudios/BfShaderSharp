using System.Buffers.Binary;

namespace BfShaderSharp.Materials;

/// <summary>Bakes a material's authored texture transform into the matrix and translation the shader's material block holds.</summary>
/// <remarks>
/// The authored form is a mode, scale x and y, a rotation in radians and a translation x and y. The block holds the 2x2
/// rotate-scale matrix then the translation, so that <c>uv' = uv * [[m0.x, m0.y], [m0.z, m0.w]] + m1.xy</c>. Mode 0 puts the
/// pivot at the centre of the texture (Maya style) and mode 1 differs in the translate term (3ds Max style); these two are
/// the modes the game's materials use. Any other mode is passed through as stored.
/// </remarks>
public static class TexSrtBake
{
    /// <summary>The 32 bytes the block holds for the authored 24 or 28 bytes of a transform.</summary>
    public static byte[] Bake(ReadOnlySpan<byte> authored)
    {
        if (authored.Length < 24) return new byte[32];
        int mode = BinaryPrimitives.ReadInt32LittleEndian(authored);
        float sx = Float(authored, 4), sy = Float(authored, 8), rotation = Float(authored, 12), tx = Float(authored, 16), ty = Float(authored, 20);
        float cos = MathF.Cos(rotation), sin = MathF.Sin(rotation);
        float x, y;
        switch (mode)
        {
            case 0:
                float half = sin * 0.5f - 0.5f;
                x = sx * (cos * -0.5f - half - tx);
                y = sy * (cos * -0.5f + half + ty) + 1.0f;
                break;
            case 1:
                x = sx * sin * (ty - 0.5f) - sx * cos * (tx + 0.5f) + 0.5f;
                y = sy * sin * (tx + 0.5f) + sy * cos * (ty - 0.5f) + 0.5f;
                break;
            default:
                return authored.ToArray();
        }

        byte[] baked = new byte[32];
        float[] values = [sx * cos, -sy * sin, sx * sin, sy * cos, x, y];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(baked.AsSpan(i * 4), values[i]);
        return baked;
    }

    private static float Float(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadSingleLittleEndian(data[at..]);
}
