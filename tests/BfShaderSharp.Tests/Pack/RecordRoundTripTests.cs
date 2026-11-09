using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;

namespace BfShaderSharp.Tests.Pack;

public class RecordRoundTripTests
{
    private static byte[] Encode(Action<RecordWriter> write)
    {
        RecordWriter writer = new();
        write(writer);
        return writer.ToArray();
    }

    // Writing what was read gives the same bytes, so nothing the record holds is dropped or reordered.
    private static void AssertRoundTrips<T>(T record, Action<T, RecordWriter> write, Func<RecordReader, T> read)
    {
        byte[] bytes = Encode(w => write(record, w));
        Assert.Equal(bytes, Encode(w => write(read(new RecordReader(bytes)), w)));
    }

    [Fact]
    public void ATextureRoundTrips() =>
        AssertRoundTrips(new TextureRecord(new BlobKey(1, 2), 3, 0x1b01, 1024, 512, 1, 6, 11), (r, w) => r.Write(w), TextureRecord.Read);

    [Fact]
    public void ALinearTextureRoundTripsItsLevelSizes() =>
        AssertRoundTrips(new TextureRecord(new BlobKey(1, 2), 0, 0x1b01, 64, 64, 1, 2, 3, TextureStorage.Linear, [4096u, 1024u, 256u, 4096u, 1024u, 256u]), (r, w) => r.Write(w), TextureRecord.Read);

    [Fact]
    public void AMaterialRoundTrips() =>
        AssertRoundTrips(new MaterialRecord("Model", "model", 42, 1,
            [new NamePair("shadows", "1")],
            [new RenderInfoRecord("gsys_pass", 2, [], [], ["seal"]), new RenderInfoRecord("alpha", 1, [], [0.5f], []), new RenderInfoRecord("mode", 0, [3, 4], [], [])],
            [new SamplerRecord("albedo", "Tex_A", 1, 1, 2, 0, 1, 3, 42, 0, 13, -0.5f)],
            [new NamePair("tex_albedo", "albedo")],
            [new ParamRecord("tint", 4, 16)],
            new byte[] { 1, 2, 3, 4 }, 5, 6, new BlobKey(7, 8), new BlobKey(9, 10)), (r, w) => r.Write(w), MaterialRecord.Read);

    [Fact]
    public void AUniformLayoutRoundTrips() =>
        AssertRoundTrips(new UniformLayoutRecord("gsys_material", 976, [new UniformOffset("ns_tex_mtx0", 0), new UniformOffset("ns_param_color0", 128)]), (r, w) => r.Write(w), UniformLayoutRecord.Read);

    private static CurveRecord Curve(ushort flags) => new(flags, 8, 0, 30, 0.5f, 1, 0, 0, [0, 10, 30], [1, 2, 3], [4], [1, 0], [9, 8, 7]);

    [Fact]
    public void ASkeletalAnimationRoundTrips() =>
        AssertRoundTrips(new SkeletalAnimRecord("Wait", 0x1204, 30, [new BoneAnimRecord("Root", 0xf8, 1, 2, 3, 4, [1, 1, 1, 0, 0, 0, 1, 0, 0, 0], [Curve(0x480a)]), new BoneAnimRecord("Hip", 0, 0, 0, 0, -1, [], [])]), (r, w) => r.Write(w), SkeletalAnimRecord.Read);

    [Fact]
    public void AMaterialAnimationRoundTrips() =>
        AssertRoundTrips(new MaterialAnimRecord(3, "Scroll", 5, 60,
            [new MaterialAnimItemRecord("Mt_Eye", [new ParamAnimInfoRecord("p_tex_srt1", 0, 1, 0, 0, 1, 2)], [new PatternAnimInfoRecord("albedo", -1, 3, -1)], [Curve(0x10)], [new ConstantRecord(0, 7)], [4, 8])], ["Tex_A"]), (r, w) => r.Write(w), MaterialAnimRecord.Read);

    [Fact]
    public void AVisibilityAnimationRoundTrips() =>
        AssertRoundTrips(new VisibilityAnimRecord(1, "Show", 4, 10, ["Hat", "Cap"], [1, 0], [Curve(0x60)]), (r, w) => r.Write(w), VisibilityAnimRecord.Read);

    [Fact]
    public void AShapeAnimationRoundTrips() =>
        AssertRoundTrips(new ShapeAnimRecord("Blink", 4, 20, [new VertexShapeAnimRecord("Face", [new KeyShapeInfoRecord("Closed", 0, -1)], [Curve(0x10)], [0.5f])]), (r, w) => r.Write(w), ShapeAnimRecord.Read);

    [Fact]
    public void AShapeRoundTrips() =>
        AssertRoundTrips(new ShapeRecord(100, 2,
            [new AttribRecord("_p0", 0x201, 0, 0), new AttribRecord("_n0", 0x202, 12, 1)],
            [new StreamRecord(16, new BlobKey(5, 6)), new StreamRecord(8, new BlobKey(7, 8))],
            [new MeshRecord(4, 1, 0, [(0u, 30u), (30u, 12u)], new BlobKey(9, 10), 42)],
            [1.5f], [1, 2, 3, 4], [0, 3, 9]), (r, w) => r.Write(w), ShapeRecord.Read);

    [Fact]
    public void ASkeletonRoundTrips() =>
        AssertRoundTrips(new SkeletonRecord(7,
            [new BoneRecord("root", -1, 3, 0, -1, -1, [1, 1, 1], [0, 0, 0, 1], [0, 1, 0]), new BoneRecord("arm", 0, 3, 1, -1, -1, [1, 1, 1], [0, 0, 1, 0], [2, 0, 0])],
            [0, 1], [Enumerable.Range(0, 12).Select(i => (float)i).ToArray()]), (r, w) => r.Write(w), SkeletonRecord.Read);

    [Fact]
    public void AModelRoundTrips() =>
        AssertRoundTrips(new ModelRecord("Link", 1, 5000, new BlobKey(1, 1),
            [new ShapeRef("body", 0, 2, new BlobKey(2, 2))], [new MaterialRef("Mt_Body", new BlobKey(3, 3))]), (r, w) => r.Write(w), ModelRecord.Read);

    [Fact]
    public void MaterialsThatDifferOnlyInTheirNameEncodeAlike()
    {
        MaterialRecord record = new("A", "m", 1, 0, [], [], [], [], [], ReadOnlyMemory<byte>.Empty, -1, -1);
        Assert.Equal(Encode(record.Write), Encode(record.Write));
        Assert.Equal(BlobKey.Of(Encode(record.Write)), BlobKey.Of(Encode(w => (record with { }).Write(w))));
    }
}
