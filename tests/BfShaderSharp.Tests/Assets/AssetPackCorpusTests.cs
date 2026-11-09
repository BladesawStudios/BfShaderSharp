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
            using (FileStream stream = File.Create(path)) result = AssetPreparer.Prepare(new AssetOptions(romfs, ["Land_Deku*", "Animal_Cat*"]), stream);
            try
            {
                output.WriteLine($"{result.Models} models, {result.Materials} materials, {result.Textures} textures, {result.BlobsAdded} blobs offered, {result.BlobsStored} stored, {result.PackBytes / 1048576.0:F1} MB");
                Assert.Empty(result.Failures);
                Assert.True(result.Models > 0);
                Assert.True(result.BlobsStored < result.BlobsAdded);
                using AssetPack pack = AssetPack.Open(path);
                using ZsDic zs = ZsDic.FromRomfs(romfs);
                foreach (PackEntry entry in pack.Of(PackEntryKind.Model)) Check(pack, entry, result.MissingTextures);
                Assert.Contains(pack.Of(PackEntryKind.Model), e => pack.Model(e.Name)!.Materials.Any(m => pack.Material(m.Material).Block != default));
                CheckTexture(pack, zs);
                CheckAnimations(pack, result.Animations);
                Assert.True(result.Animations > 0);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    // Every animation the importer stored reads back, and a skeletal animation's curves keep their frames and keys.
    private static void CheckAnimations(AssetPack pack, int stored)
    {
        PackEntryKind[] kinds = [PackEntryKind.SkeletalAnim, PackEntryKind.MaterialAnim, PackEntryKind.VisibilityAnim, PackEntryKind.ShapeAnim];
        Assert.Equal(stored, kinds.Sum(kind => pack.Of(kind).Count()));
        foreach (PackEntry entry in pack.Of(PackEntryKind.SkeletalAnim).Take(50))
        {
            SkeletalAnimRecord animation = pack.SkeletalAnim(entry.Name)!;
            Assert.NotEmpty(animation.Bones);
            foreach (CurveRecord curve in animation.Bones.SelectMany(b => b.Curves))
            {
                Assert.NotEmpty(curve.Frames);
                // A cubic curve is either compressed, and decodes to a value and slope per key, or holds four plain coefficients a key.
                if ((curve.Flags & 0x4000) != 0 && (curve.Flags & 0x70) == 0 && curve.Keys.Length == 0) Assert.True(CurveDecoder.CanDecode(curve));
                if (CurveDecoder.CanDecode(curve)) Assert.All(CurveDecoder.Decode(curve).Values, v => Assert.True(float.IsFinite(v)));
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
            if (material.Block != default)
            {
                UniformLayoutRecord layout = pack.UniformLayout(material.Layout);
                byte[] block = pack.Bytes(material.Block);
                Assert.Equal(layout.Size, block.Length);
                // A plain parameter the layout names sits in the block exactly as the material holds it.
                foreach (ParamRecord param in material.Params.Where(p => p.Type is not (byte)BfresSharp.Model.Material.ShaderParamType.TexSrt and not (byte)BfresSharp.Model.Material.ShaderParamType.TexSrtEx))
                {
                    int at = layout.OffsetOf(param.Name);
                    int size = BfresSharp.Model.Material.ShaderParam.SizeOf((BfresSharp.Model.Material.ShaderParamType)param.Type);
                    if (at >= 0 && at + size <= block.Length && param.Offset + size <= material.ParamData.Length) Assert.True(material.ParamData.Span.Slice(param.Offset, size).SequenceEqual(block.AsSpan(at, size)), param.Name);
                }
            }
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

    // Linear textures decompress to the sizes their record lists, and their first level equals what the native pack's file deswizzles to.
    [CorpusFact]
    public void LinearTexturesMatchTheNativeOnesLevelForLevel()
    {
        foreach (string shaders in GameFiles.Folders)
        {
            string romfs = Path.GetDirectoryName(shaders)!;
            if (!Directory.Exists(Path.Combine(romfs, "Model"))) continue;
            string path = Path.Combine(Path.GetTempPath(), $"corpus-{Guid.NewGuid():N}.bfap");
            using (FileStream stream = File.Create(path)) AssetPreparer.Prepare(new AssetOptions(romfs, ["Land_Deku_Room*"], Storage: TextureStorage.Linear), stream);
            try
            {
                using AssetPack pack = AssetPack.Open(path);
                using ZsDic zs = ZsDic.FromRomfs(romfs);
                foreach (PackEntry entry in pack.Of(PackEntryKind.Texture).Where(e => File.Exists(romfs + "/Tex/" + e.Name + ".bntx.zs")).Take(20))
                {
                    TextureRecord record = pack.Texture(entry.Name)!;
                    Assert.Equal(TextureStorage.Linear, record.Storage);
                    byte[] levels = zs.Decompress(pack.Blob(record.File).Bytes, "x");
                    Assert.Equal(levels.LongLength, record.LevelSizes.Sum(s => (long)s));
                    BntxSharp.BntxFile native = BntxSharp.BntxFile.Load(zs.Decompress(File.ReadAllBytes(romfs + "/Tex/" + entry.Name + ".bntx.zs"), "x.zs"));
                    Assert.Equal(native.Textures[0].GetDeswizzledData(0, 0), levels.AsSpan(0, (int)record.LevelSizes[0]).ToArray());
                }
            }
            finally
            {
                File.Delete(path);
            }
        }
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
