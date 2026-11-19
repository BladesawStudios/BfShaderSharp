using BfShaderSharp.Assets;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Bnsh;
using BfShaderSharp.Decompile;
using BfShaderSharp.Errors;
using BfShaderSharp.Lookup;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using BfShaderSharp.Tests.Corpus;
using SarcSharp;
using Yaz0Sharp;

namespace BfShaderSharp.Tests.Assets;

public sealed class SwitchOneTests
{
    [Fact]
    public void ALayoutIsChosenByTheArchiveVersion()
    {
        Assert.Same(BfshaLayout.V9, BfshaLayout.For(9));
        Assert.Same(BfshaLayout.V3, BfshaLayout.For(3));
        Assert.Throws<BfShaderUnsupportedException>(() => BfshaLayout.For(5));
    }

    [Fact]
    public void ImmediateConstantsAreTheRangeTheControlBlockNames()
    {
        byte[] control = new byte[0x710];
        BitConverter.GetBytes(8u).CopyTo(control, 0x6FC);
        BitConverter.GetBytes(16u).CopyTo(control, 0x700);
        byte[] code = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        Assert.Equal(code[16..24], ImmediateConstants.Read(control, code));
        Assert.Empty(ImmediateConstants.Read(control[..0x100], code));
        Assert.Empty(ImmediateConstants.Read(control, code[..20]));
    }

    [Fact]
    public void Yaz0FilesAreDecodedWhereverAGameFileIsRead()
    {
        byte[] plain = Enumerable.Range(0, 5000).Select(i => (byte)(i % 37)).ToArray();
        byte[] packed = Yaz0.Compress(plain);
        Assert.True(GameFile.IsCompressed(packed));
        Assert.Equal(plain, GameFile.Decode(packed, "Model/X.sbfres", null));
    }

    [Fact]
    public void AFileInAPackIsReadByItsNestedPath()
    {
        SarcFile sarc = new();
        sarc.Entries.Add(new SarcEntry("Shader/a.sbfsha", [1, 2, 3]));
        sarc.Entries.Add(new SarcEntry("Model/b.sbfres", [4]));
        byte[] pack = Yaz0.Compress(sarc.Write());
        PackedFiles files = new(_ => GameFile.Decode(pack, "p.pack", null));
        string nested = PackedFiles.Join("Pack/p.pack", "Shader/a.sbfsha");
        Assert.True(PackedFiles.IsNested(nested));
        Assert.Equal(("Pack/p.pack", "Shader/a.sbfsha"), PackedFiles.Split(nested));
        Assert.Equal([1, 2, 3], files.Read(nested));
        Assert.Equal(["Shader/a.sbfsha"], files.Entries("Pack/p.pack", "Shader/"));
        Assert.Throws<FileNotFoundException>(() => files.Read(PackedFiles.Join("Pack/p.pack", "Shader/none")));
    }

    // Every shader archive in the pack reads: each program is found by its own choices, its variation and bindings are in range.
    [BotwFact]
    public void EveryBotwProgramIsFoundByItsOwnChoices()
    {
        using RomfsFiles romfs = new(GameRomfs.Botw!);
        IReadOnlyList<string> archives = romfs.ShaderFiles();
        Assert.Equal(9, archives.Count);
        foreach (string path in archives)
        {
            BfshaFile file = BfshaFile.Read(romfs.Read(path));
            foreach (ShaderModel model in file.Models())
            {
                ProgramLookup lookup = model.CreateLookup();
                int variations = model.Bnsh.VariationCount;
                for (int program = 0; program < model.ProgramCount; program++)
                {
                    int found = lookup.Find(lookup.ChoicesOf(program));
                    Assert.True(found >= 0, $"{path} {model.Name}: program {program}");
                    ShaderProgram p = model.Program(found);
                    Assert.InRange(p.VariationIndex, 0, variations - 1);
                    for (int i = 0; i < p.SamplerCount; i++) _ = p.Sampler(i);
                    for (int i = 0; i < p.UniformBlockCount; i++) _ = p.UniformBlock(i);
                }
            }
        }
    }

    [BotwFact]
    public void TheBotwMaterialArchiveHasTheShapeItWasMeasuredWith()
    {
        using RomfsFiles romfs = new(GameRomfs.Botw!);
        string path = romfs.ShaderFiles().Single(p => p.EndsWith("uking_mat.product.sbfsha"));
        ShaderModel model = BfshaFile.Read(romfs.Read(path)).Model("uking_mat")!.Value;
        Assert.Equal(14148, model.ProgramCount);
        Assert.Equal(407, model.StaticOptions.Count);
        Assert.Equal(4, model.DynamicOptions.Count);
        Assert.Equal(33, model.Samplers.Count);
        Assert.Equal(10, model.UniformBlocks.Count);
        Assert.Equal(51, model.StaticKeyLength);
        Assert.Equal(1, model.DynamicKeyLength);
        Assert.Equal(model.ProgramCount, model.Bnsh.VariationCount);
    }

    // A spread of programs of every Breath of the Wild archive decompiles in both stages.
    [BotwFact]
    public void BotwStagesDecompile()
    {
        using RomfsFiles romfs = new(GameRomfs.Botw!);
        foreach (string path in romfs.ShaderFiles())
            AssertStagesDecompile(BfshaFile.Read(romfs.Read(path)), 97);
    }

    [TotkFact]
    public void TotkStagesDecompile()
    {
        using RomfsFiles romfs = new(GameRomfs.Totk!);
        foreach (string path in romfs.ShaderFiles())
            AssertStagesDecompile(BfshaFile.Read(romfs.Read(path)), 211);
    }

    // The stage code says which instruction set it is: Switch archives are Maxwell, and a Maxwell stage is translated as it is.
    private static void AssertStagesDecompile(BfshaFile file, int stride)
    {
        foreach (ShaderModel model in file.Models())
        {
            BnshFile bnsh = model.Bnsh;
            for (int program = 0; program < model.ProgramCount; program += stride)
            {
                ShaderProgramBinary binary = bnsh.Variation(model.Program(program).VariationIndex).Program;
                foreach (ShaderStage stage in new[] { ShaderStage.Vertex, ShaderStage.Fragment })
                {
                    if (binary.Code(stage) is not { } code) continue;
                    Assert.Equal(ShaderArchitecture.Maxwell, code.Architecture);
                    try
                    {
                        Assert.Contains("void main()", ShaderDecompiler.ToGlsl(code));
                    }
                    catch (BfShaderFormatException exception)
                    {
                        throw new Xunit.Sdk.XunitException($"{model.Name} program {program} {stage}: {exception.Message}");
                    }
                }
            }
        }
    }

    [BotwFact]
    public void ABotwModelPreparesWithItsProgramsAndEmbeddedTextures()
    {
        string path = Path.Combine(Path.GetTempPath(), $"botw-{Guid.NewGuid():N}.bfap");
        try
        {
            AssetResult result;
            using (FileStream stream = File.Create(path)) result = AssetPreparer.Prepare(new AssetOptions(GameRomfs.Botw!, ["Animal_Bass*"]), stream);
            Assert.Empty(result.Failures);
            Assert.Empty(result.MissingTextures);
            Assert.Equal(4, result.Models);
            using AssetPack pack = AssetPack.Open(path);
            ModelRecord model = pack.Model("Animal_Bass/Bass")!;
            Assert.Equal(2, model.Materials.Count);
            foreach (MaterialRef reference in model.Materials)
            {
                MaterialRecord material = pack.Material(reference.Material);
                Assert.Equal("uking_mat", material.ShaderArchive);
                Assert.True(material.Program >= 0, reference.Name);
                foreach (string texture in material.Samplers.Select(s => s.Texture).Where(n => n != ""))
                    Assert.NotNull(pack.Texture(texture));
            }

            Assert.Equal(8883, pack.Material(model.Materials.Single(m => m.Name == "Mt_Body").Material).Program);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TotkFact]
    public void ATotkModelPreparesWithItsTxtgTextures()
    {
        string path = Path.Combine(Path.GetTempPath(), $"totk-{Guid.NewGuid():N}.bfap");
        try
        {
            AssetResult result;
            using (FileStream stream = File.Create(path)) result = AssetPreparer.Prepare(new AssetOptions(GameRomfs.Totk!, ["Npc_Zelda_AncientHyrule*"]), stream);
            Assert.Empty(result.Failures);
            Assert.Empty(result.MissingTextures);
            Assert.Equal(47, result.Textures);
            using AssetPack pack = AssetPack.Open(path);
            foreach (PackEntry entry in pack.Of(PackEntryKind.Texture))
            {
                TextureRecord texture = pack.Texture(entry.Name)!;
                Assert.True(texture.Width > 0 && texture.Height > 0, entry.Name);
                Assert.True(texture.Format >= 0x200, entry.Name);
                if (texture.Storage == TextureStorage.Linear) Assert.Equal(texture.ArrayLength * texture.MipCount, texture.LevelSizes.Count);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
