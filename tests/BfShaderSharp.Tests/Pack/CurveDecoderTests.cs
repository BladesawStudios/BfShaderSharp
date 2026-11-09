using BfShaderSharp.Pack.Records;

namespace BfShaderSharp.Tests.Pack;

public class CurveDecoderTests
{
    private static CurveRecord Curve(ushort flags, float[] frames, byte[] keys, float scale, float offset) =>
        new(flags, 0, frames[0], frames[^1], scale, offset, 0, 0, frames, [], [], [], keys);

    private static byte[] Header(float slopeScale, float slopeOffset) => [.. BitConverter.GetBytes(slopeScale), .. BitConverter.GetBytes(slopeOffset)];

    [Fact]
    public void KeysDecodeToAValueAndASlopeEach()
    {
        CurveRecord curve = Curve(0x480a, [0, 10], [.. Header(0.5f, -1f), 0, 2, 100, 4], 0.01f, 1f);
        HermiteKeys keys = CurveDecoder.Decode(curve);
        Assert.Equal([1f, 2f], keys.Values);
        Assert.Equal([0f, 1f], keys.Slopes);
    }

    [Fact]
    public void SixteenBitKeysAreWordsAndTheSplineHitsItsKeys()
    {
        byte[] data = [.. Header(1f, 0f), .. BitConverter.GetBytes((ushort)1000), .. BitConverter.GetBytes((ushort)0), .. BitConverter.GetBytes((ushort)3000), .. BitConverter.GetBytes((ushort)0)];
        HermiteKeys keys = CurveDecoder.Decode(Curve(0x4805, [0, 20], data, 0.001f, 0f));
        Assert.Equal(1f, keys.Evaluate(0), 4);
        Assert.Equal(3f, keys.Evaluate(20), 4);
        Assert.Equal(2f, keys.Evaluate(10), 4);
        Assert.Equal(3f, keys.Evaluate(99), 4);
    }

    [Fact]
    public void ACurveOfAnotherFormIsNotDecoded()
    {
        Assert.False(CurveDecoder.CanDecode(Curve(0x10, [0, 1], [], 1, 0)));
        Assert.Throws<InvalidOperationException>(() => CurveDecoder.Decode(Curve(0x10, [0, 1], [], 1, 0)));
    }
}
