using BfShaderSharp.Assets;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using BfShaderSharp.Tests.Corpus;

namespace BfShaderSharp.Tests.Assets;

public class ModelAssetPreparerTests
{
    // A model file prepared on its own, as a modded one is, gets the same programs, blocks and textures as in the pack of the whole dump.
    [CorpusFact]
    public void AModelFileOnItsOwnPreparesLikeItsPlaceInTheDump()
    {
        foreach (string shaders in GameFiles.Folders)
        {
            string romfs = Path.GetDirectoryName(shaders)!;
            if (!Directory.Exists(Path.Combine(romfs, "Model"))) continue;
            string file = new RomfsFiles(romfs).ModelFiles(["Animal_Cat*"])[0];
            string whole = Path.Combine(Path.GetTempPath(), $"mod-whole-{Guid.NewGuid():N}.bfap");
            string alone = Path.Combine(Path.GetTempPath(), $"mod-alone-{Guid.NewGuid():N}.bfap");
            try
            {
                using (FileStream stream = File.Create(whole)) AssetPreparer.Prepare(new AssetOptions(romfs, [RomfsFiles.StemOf(file)]), stream);
                AssetResult result;
                using (RomfsFiles files = new(romfs))
                using (FileStream stream = File.Create(alone)) result = ModelAssetPreparer.Prepare(new AssetOptions(romfs, []), RomfsFiles.StemOf(file), files.Read(file), [], stream);
                Assert.Empty(result.Failures);
                using AssetPack a = AssetPack.Open(whole);
                using AssetPack b = AssetPack.Open(alone);
                PackEntry entry = a.Of(PackEntryKind.Model).First();
                ModelRecord expected = a.Model(entry.Name)!;
                ModelRecord actual = b.Model(entry.Name)!;
                Assert.Equal(expected.Materials.Count, actual.Materials.Count);
                for (int i = 0; i < expected.Materials.Count; i++)
                {
                    MaterialRecord x = a.Material(expected.Materials[i].Material);
                    MaterialRecord y = b.Material(actual.Materials[i].Material);
                    Assert.Equal((x.ShadingModel, x.Program, x.Block), (y.ShadingModel, y.Program, y.Block));
                }

                Assert.Equal(a.Of(PackEntryKind.Texture).Count(), b.Of(PackEntryKind.Texture).Count());
            }
            finally
            {
                File.Delete(whole);
                File.Delete(alone);
            }
        }
    }
}
