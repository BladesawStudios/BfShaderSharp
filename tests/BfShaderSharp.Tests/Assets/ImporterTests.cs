using BntxSharp;
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
    private readonly AssetContext _context;

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
    public void ShapesWithAnOutOfRangeVertexBufferAreAFormatError()
    {
        ResModel model = Model("Mt_A", new byte[12]);
        model.Shapes.Values[0].VertexBufferIndex = 5;
        Assert.Throws<BfShaderSharp.Errors.BfShaderFormatException>(() => ShapeImporter.Import(_writer, model, model.Shapes.Values[0]));
    }
}
