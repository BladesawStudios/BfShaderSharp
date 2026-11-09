using System.Buffers.Binary;

namespace BfShaderSharp.Pack.Records;

/// <summary>The keys of a compressed cubic curve as a value and a slope at each frame.</summary>
/// <remarks>Between two keys the value is the cubic Hermite spline through both values with both slopes, the slope being per frame.</remarks>
public sealed record HermiteKeys(float[] Frames, float[] Values, float[] Slopes)
{
    /// <summary>The curve's value at a frame, clamped to the first and last keys.</summary>
    public float Evaluate(float frame)
    {
        int last = Frames.Length - 1;
        if (last < 0) return 0;
        if (frame <= Frames[0]) return Values[0];
        if (frame >= Frames[last]) return Values[last];
        int segment = Array.BinarySearch(Frames, frame);
        if (segment < 0) segment = ~segment - 1;
        while (segment < last && Frames[segment + 1] <= frame) segment++;
        float span = Frames[segment + 1] - Frames[segment];
        if (span <= 0) return Values[segment + 1];
        float t = (frame - Frames[segment]) / span, t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * Values[segment] + (t3 - 2 * t2 + t) * span * Slopes[segment]
            + (-2 * t3 + 3 * t2) * Values[segment + 1] + (t3 - t2) * span * Slopes[segment + 1];
    }
}

/// <summary>The keys of a rotation curve: a unit quaternion (x, y, z, w) at each frame.</summary>
/// <remarks>Between two keys the quaternions are interpolated along the shorter arc.</remarks>
public sealed record QuaternionKeys(float[] Frames, float[][] Values)
{
    /// <summary>The rotation at a frame as x, y, z, w, clamped to the first and last keys.</summary>
    public float[] Evaluate(float frame)
    {
        int last = Frames.Length - 1;
        if (last < 0) return [0, 0, 0, 1];
        if (frame <= Frames[0]) return Values[0];
        if (frame >= Frames[last]) return Values[last];
        int segment = 0;
        while (segment < last - 1 && Frames[segment + 1] <= frame) segment++;
        float span = Frames[segment + 1] - Frames[segment];
        float t = span <= 0 ? 1 : (frame - Frames[segment]) / span;
        float[] a = Values[segment], b = Values[segment + 1];
        float dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3];
        float sign = dot < 0 ? -1 : 1;
        float wa = 1 - t, wb = t * sign;
        if (MathF.Abs(dot) < 0.9995f)
        {
            float angle = MathF.Acos(MathF.Abs(dot)), sin = MathF.Sin(angle);
            wa = MathF.Sin((1 - t) * angle) / sin;
            wb = MathF.Sin(t * angle) / sin * sign;
        }

        float[] result = [wa * a[0] + wb * b[0], wa * a[1] + wb * b[1], wa * a[2] + wb * b[2], wa * a[3] + wb * b[3]];
        float length = MathF.Sqrt(result[0] * result[0] + result[1] * result[1] + result[2] * result[2] + result[3] * result[3]);
        return length > 0 ? [result[0] / length, result[1] / length, result[2] / length, result[3] / length] : [0, 0, 0, 1];
    }
}

/// <summary>Decodes the compressed cubic curves of Switch 2 model files.</summary>
/// <remarks>
/// A curve of float keys holds a float value and a float slope for each key and nothing else. Otherwise the key bytes are two floats, then for each key an unsigned value and an unsigned slope, a byte each for 8-bit curves
/// and a 16-bit little-endian word each for 16-bit ones. The value is <c>q * Scale + Offset</c>, with the curve's own scale
/// and offset, and the slope is <c>q * h0 + h1</c> with the two header floats. (Found from the model files by fitting: the
/// decoded slopes agree with the differences of the decoded values, median correlation 0.98 over 136,000 curves.)
/// </remarks>
public static class CurveDecoder
{
    private const ushort CompressedFlag = 0x4000;
    private const ushort CurveTypeMask = 0b111_0000;
    private const ushort KeyTypeMask = 0b1100;
    private const ushort Int16Keys = 1 << 2;
    private const ushort ByteKeys = 2 << 2;

    /// <summary>Whether the curve is a compressed cubic curve, the form this decodes.</summary>
    public static bool CanDecode(CurveRecord curve) =>
        (curve.Flags & CompressedFlag) != 0 && (curve.Flags & CurveTypeMask) == 0 && curve.CompressedKeys.Length == curve.Frames.Length * 2 * Width(curve) + (Narrow(curve) ? 8 : 0);

    private static bool Narrow(CurveRecord curve) => (curve.Flags & KeyTypeMask) is Int16Keys or ByteKeys;

    private static int Width(CurveRecord curve) => (curve.Flags & KeyTypeMask) switch { Int16Keys => 2, ByteKeys => 1, _ => 4 };

    /// <summary>Whether the curve is a rotation curve holding four floats a key, which the file marks with the plain keys flag; <see cref="DecodeRotation"/> reads it.</summary>
    /// <remarks>These curves are not cubic polynomials: every key is a unit quaternion (checked over the OOT files), and they animate the rotation of a bone stored as a quaternion.</remarks>
    public static bool IsRotation(CurveRecord curve) => (curve.Flags & 0x80) != 0 && curve.Keys.Length == curve.Frames.Length * 4 && curve.Frames.Length > 0;

    public static QuaternionKeys DecodeRotation(CurveRecord curve)
    {
        if (!IsRotation(curve)) throw new InvalidOperationException("The curve is not a rotation curve");
        return new QuaternionKeys(curve.Frames, [.. Enumerable.Range(0, curve.Frames.Length).Select(i => curve.Keys[(i * 4)..(i * 4 + 4)])]);
    }

    public static HermiteKeys Decode(CurveRecord curve)
    {
        if (!CanDecode(curve)) throw new InvalidOperationException("The curve is not a compressed cubic curve of 8-bit or 16-bit keys");
        ReadOnlySpan<byte> data = curve.CompressedKeys;
        if (!Narrow(curve))
        {
            float[] plainValues = new float[curve.Frames.Length], plainSlopes = new float[curve.Frames.Length];
            for (int i = 0; i < plainValues.Length; i++)
            {
                plainValues[i] = BinaryPrimitives.ReadSingleLittleEndian(data[(i * 8)..]);
                plainSlopes[i] = BinaryPrimitives.ReadSingleLittleEndian(data[(i * 8 + 4)..]);
            }

            return new HermiteKeys(curve.Frames, plainValues, plainSlopes);
        }

        float slopeScale = BinaryPrimitives.ReadSingleLittleEndian(data), slopeOffset = BinaryPrimitives.ReadSingleLittleEndian(data[4..]);
        bool wide = (curve.Flags & KeyTypeMask) == Int16Keys;
        int count = curve.Frames.Length;
        float[] values = new float[count], slopes = new float[count];
        for (int i = 0; i < count; i++)
        {
            int at = 8 + i * (wide ? 4 : 2);
            (uint value, uint slope) = wide ? (BinaryPrimitives.ReadUInt16LittleEndian(data[at..]), BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 2)..])) : (data[at], data[at + 1]);
            values[i] = value * curve.Scale + curve.Offset;
            slopes[i] = slope * slopeScale + slopeOffset;
        }

        return new HermiteKeys(curve.Frames, values, slopes);
    }
}
