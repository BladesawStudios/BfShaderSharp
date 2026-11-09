using BfShaderSharp.Assets;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using BfShaderSharp.Preparation;
using BfShaderSharp.Prepared;
using BfShaderSharp.Tests.Corpus;
using Xunit.Abstractions;
using ZsDicSharp;

namespace BfShaderSharp.Tests.Assets;

public class AssetPackCorpusTests(ITestOutputHelper output)
{
    // A group of model files prepared into a pack: every record reads back, every blob a record names is in the pack, geometry sizes agree
    // with the records, every sampled texture is stored or reported missing, and sharing shows up as fewer blobs stored than offered.
    [CorpusFact]
    public void AGroupOfModelsPreparesIntoAPackThatReadsBack()
    {
        foreach (string shaders in GameFiles.Folders)
        {
            string romfs = Path.GetDirectoryName(shaders)!;
            if (!Directory.Exists(Path.Combine(romfs, "Model"))) continue;
            string path = Path.Combine(Path.GetTempPath(), $"corpus-{Guid.NewGuid():N}.bfap");
            AssetResult result;
            using (FileStream stream = File.Create(path)) result = AssetPreparer.Prepare(new AssetOptions(romfs, ["Land_Deku*"]), stream);
            try
            {
                output.WriteLine($"{result.Models} models, {result.Materials} materials, {result.Textures} textures, {result.BlobsAdded} blobs offered, {result.BlobsStored} stored, {result.PackBytes / 1048576.0:F1} MB");
                Assert.Empty(result.Failures);
                Assert.True(result.Models > 0);
                Assert.True(result.BlobsStored < result.BlobsAdded);
                using AssetPack pack = AssetPack.Open(path);
                using ZsDic zs = ZsDic.FromRomfs(romfs);
                foreach (PackEntry entry in pack.Of(PackEntryKind.Model)) Check(pack, entry, result.MissingTextures);
                CheckTexture(pack, zs);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    private static void Check(AssetPack pack, PackEntry entry, IReadOnlyList<string> missing)
    {
        ModelRecord model = pack.Model(entry.Name)!;
        Assert.NotEmpty(pack.Skeleton(model.Skeleton).Bones);
        foreach (ShapeRef reference in model.Shapes)
        {
            ShapeRecord shape = pack.Shape(reference.Shape);
            Assert.True(reference.MaterialIndex < model.Materials.Count);
            foreach (StreamRecord stream in shape.Streams) Assert.True(pack.Bytes(stream.Data).Length >= shape.VertexCount * stream.Stride);
            foreach (MeshRecord mesh in shape.Meshes) Assert.Equal(mesh.IndexCount * (mesh.IndexFormat switch { 0 => 1, 1 => 2, _ => 4 }), (uint)pack.Bytes(mesh.Indices).Length);
        }

        foreach (MaterialRef reference in model.Materials)
        {
            MaterialRecord material = pack.Material(reference.Material);
            Assert.True(material.Program >= -1);
            foreach (SamplerRecord sampler in material.Samplers.Where(s => s.Texture != "")) Assert.True(pack.Texture(sampler.Texture) is not null || missing.Contains(sampler.Texture), sampler.Texture);
        }
    }

    // A texture's frame decompresses, with the dictionary named by its header, to the size the pack records and a BNTX file.
    private static void CheckTexture(AssetPack pack, ZsDic zs)
    {
        PackEntry entry = pack.Of(PackEntryKind.Texture).First();
        TextureRecord texture = pack.Texture(entry.Name)!;
        BlobData frame = pack.Blob(texture.File);
        byte[] bntx = frame.Codec == BlobCodec.ZstdFrame ? zs.Decompress(frame.Bytes, "texture.zs") : frame.Bytes;
        Assert.Equal(frame.RawSize, bntx.Length);
        Assert.True(BntxSharp.BntxFile.IsBntx(bntx));
    }

    // An archive prepared for some programs has the code of those programs and leaves the rest empty.
    [CorpusFact]
    public void ArchivesPreparedForSomeProgramsHaveOnlyThose()
    {
        foreach (string shaders in GameFiles.Folders)
        {
            (string _, byte[] data) = GameFiles.BfshaFiles(shaders, 1).First(f => f.Name.Contains("Foliage", StringComparison.Ordinal));
            BfshaFile archive = BfshaFile.Read(data);
            ShaderModel model = archive.Models().First(m => Enumerable.Range(0, m.ProgramCount).Any(p => m.Program(p).VariationIndex != m.Program(0).VariationIndex));
            int first = 0;
            int other = Enumerable.Range(0, model.ProgramCount).First(p => model.Program(p).VariationIndex != model.Program(first).VariationIndex);
            using MemoryStream stream = new();
            ArchivePreparer.Prepare(archive, stream, 1, 0, null, new Dictionary<string, IReadOnlySet<int>> { [model.Name] = new HashSet<int> { first } });
            PreparedModel prepared = PreparedArchive.Read(stream.ToArray()).Model(model.Name)!;
            Assert.NotNull(prepared.Glsl(first, ShaderStage.Fragment));
            Assert.Null(prepared.Glsl(other, ShaderStage.Fragment));
        }
    }
}
