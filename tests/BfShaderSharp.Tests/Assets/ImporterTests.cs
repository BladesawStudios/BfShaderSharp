using BntxSharp;
using BfresSharp.Model.Animation;
using BfresSharp.Model;
using BfresSharp.Model.Geometry;
using BfresSharp.Model.Material;
using BfShaderSharp.Assets;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;

namespace BfShaderSharp.Tests.Assets;

public sealed class ImporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"imp-{Guid.NewGuid():N}");
    private readonly string _pack;
    private readonly FileStream _stream;
    private readonly RomfsFiles _romfs;
    private readonly PackWriter _writer;
    private AssetContext _context;

    public ImporterTests()
    {
        Directory.CreateDirectory(_root);
        _pack = Path.Combine(_root, "test.bfap");
        _stream = File.Create(_pack);
        _romfs = new RomfsFiles(_root);
        _writer = new PackWriter(_stream);
        _context = new AssetContext(_writer, _romfs, new ShaderArchiveSet(), new Dictionary<ulong, string>());
    }

    public void Dispose()
    {
        _stream.Dispose();
        _romfs.Dispose();
        Directory.Delete(_root, true);
    }

    private static ResModel Model(string materialName, byte[] vertices)
    {
        ResModel model = new() { Name = "m" };
        VertexBuffer buffer = new() { VertexCount = 3 };
        buffer.Attributes.Add("_p0", new VertexAttrib { Name = "_p0" });
        buffer.Streams.Add(new VertexStream { Stride = 4, Data = vertices });
        model.VertexBuffers.Add(buffer);
        Shape shape = new() { Name = "s", VertexSkinCount = 1 };
        shape.Meshes.Add(new Mesh { IndexData = new byte[] { 0, 0, 1, 0, 2, 0 }, IndexFormat = IndexFormat.UInt16 });
        model.Shapes.Add("s", shape);
        Material material = new() { Name = materialName };
        material.ShaderAssign.ShaderArchiveName = "Nothing";
        material.Samplers.Add("albedo", new Sampler());
        material.TextureRefs.Add("Tex_A");
        model.Materials.Add(materialName, material);
        return model;
    }

    [Fact]
    public void AModelIsStoredAsRecordsThatPointAtBlobs()
    {
        BlobKey key = ModelImporter.Import(_context, Model("Mt_A", new byte[12]));
        _writer.AddEntry(PackEntryKind.Model, "m", key);
        _writer.Finish();
        _stream.Dispose();
        using AssetPack pack = AssetPack.Open(_pack);
        ModelRecord model = pack.Model("m")!;
        Assert.Equal(["s"], model.Shapes.Select(s => s.Name));
        ShapeRecord shape = pack.Shape(model.Shapes[0].Shape);
        Assert.Equal(12, pack.Bytes(shape.Streams[0].Data).Length);
        Assert.Equal(3u, shape.Meshes[0].IndexCount);
        MaterialRecord material = pack.Material(model.Materials[0].Material);
        Assert.Equal("Tex_A", material.Samplers[0].Texture);
        Assert.Equal(-1, material.Program);
        Assert.True(_context.UsedTextures.ContainsKey("Tex_A"));
    }

    [Fact]
    public void TwoModelsThatDifferOnlyInMaterialNamesShareEverythingElse()
    {
        BlobKey first = ModelImporter.Import(_context, Model("Mt_A", new byte[12]));
        int blobs = _writer.UniqueBlobs;
        BlobKey second = ModelImporter.Import(_context, Model("Mt_B", new byte[12]));
        Assert.NotEqual(first, second);
        Assert.Equal(blobs + 1, _writer.UniqueBlobs);
    }

    [Fact]
    public void ATextureFileIsStoredOnceWithAnEntryForEachTexture()
    {
        BntxFile file = new();
        file.Textures.Add(new BntxTexture { Name = "Tex_A", Width = 4, Height = 4, Format = SurfaceFormat.R8_UNORM, Data = new byte[64], MipOffsets = { 0 } });
        string path = Path.Combine(_root, "Tex_A.bntx");
        File.WriteAllBytes(path, file.Save());
        Assert.Equal(1, TextureImporter.Import(_context, path));
        _writer.Finish();
        _stream.Dispose();
        using AssetPack pack = AssetPack.Open(_pack);
        TextureRecord texture = pack.Texture("Tex_A")!;
        Assert.Equal(4, texture.Width);
        Assert.Equal((uint)SurfaceFormat.R8_UNORM, texture.Format);
        Assert.True(pack.Has(texture.File));
    }

    [Fact]
    public void ALinearTextureIsOneFrameOfDeswizzledLevelsWithTheirSizes()
    {
        _context = new AssetContext(_writer, _romfs, new ShaderArchiveSet(), new Dictionary<ulong, string>(), TextureStorage.Linear, 1);
        BntxFile file = new();
        BntxTexture texture = new() { Name = "Tex_L", Width = 4, Height = 4, Format = SurfaceFormat.R8_UNORM, Data = new byte[4096], MipOffsets = { 0 } };
        for (int i = 0; i < 16; i++) texture.Data[i] = (byte)(i + 1);
        file.Textures.Add(texture);
        string path = Path.Combine(_root, "Tex_L.bntx");
        File.WriteAllBytes(path, file.Save());
        Assert.Equal(1, TextureImporter.Import(_context, path));
        _writer.Finish();
        _stream.Dispose();
        using AssetPack pack = AssetPack.Open(_pack);
        TextureRecord record = pack.Texture("Tex_L")!;
        Assert.Equal(TextureStorage.Linear, record.Storage);
        Assert.Equal([16u], record.LevelSizes);
        BlobData frame = pack.Blob(record.File);
        Assert.Equal(BlobCodec.ZstdFrame, frame.Codec);
        Assert.Equal(16, frame.RawSize);
        Assert.Equal(texture.GetDeswizzledData(0, 0), _romfs.Dictionaries.Decompress(frame.Bytes, "x"));
    }

    [Fact]
    public void AnimationsAreStoredByFileAndNameWithTheirCurves()
    {
        ResFile file = new();
        SkeletalAnim skeletal = new() { Name = "Wait", FrameCount = 10 };
        BoneAnim bone = new() { Name = "Root", BaseData = new BoneAnimData { Scale = new(1, 1, 1), Rotate = new(0, 0, 0, 1), Translate = new(1, 2, 3) } };
        bone.Curves.Add(new AnimCurve { Frames = [0, 10], Keys = [1, 2, 3, 4, 5, 6, 7, 8], AnimDataOffset = 4 });
        skeletal.BoneAnims.Add(bone);
        file.SkeletalAnims.Add("Wait", skeletal);
        MaterialAnim scroll = new() { Name = "Scroll", FrameCount = 5 };
        MaterialAnimItem item = new() { Name = "Mt_A" };
        item.ParamAnimInfos.Add(new ParamAnimInfo { Name = "p_tex_srt0", FloatCurveCount = 1 });
        scroll.Items.Add(item);
        file.TexSrtAnims.Add("Scroll", scroll);
        Assert.Equal(2, AnimationImporter.Import(_context, "Link", file));
        _writer.Finish();
        _stream.Dispose();
        using AssetPack pack = AssetPack.Open(_pack);
        SkeletalAnimRecord wait = pack.SkeletalAnim("Link/Wait")!;
        Assert.Equal(10, wait.FrameCount);
        Assert.Equal([1f, 1, 1, 0, 0, 0, 1, 1, 2, 3], wait.Bones[0].Base);
        Assert.Equal([1f, 2, 3, 4, 5, 6, 7, 8], wait.Bones[0].Curves[0].Keys);
        MaterialAnimRecord material = pack.MaterialAnim("Link/Scroll")!;
        Assert.Equal(3, material.Kind);
        Assert.Equal("p_tex_srt0", material.Items[0].Params[0].Name);
    }

    [Fact]
    public void ShapesWithAnOutOfRangeVertexBufferAreAFormatError()
    {
        ResModel model = Model("Mt_A", new byte[12]);
        model.Shapes.Values[0].VertexBufferIndex = 5;
        Assert.Throws<BfShaderSharp.Errors.BfShaderFormatException>(() => ShapeImporter.Import(_writer, model, model.Shapes.Values[0]));
    }
}
