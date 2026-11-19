using System.Text;
using BfShaderSharp.Assets;
using BfShaderSharp.Pack;
using BfShaderSharp.Tests.Corpus;

namespace BfShaderSharp.Tests.Assets;

/// <summary>The importers of the data that belongs to no model, run on a Tears of the Kingdom romfs (BFS_ROMFS).</summary>
public sealed class SystemImportTests
{
    private static AssetPack Prepare(string[] tiles, string[] actors, string[] effectTextures, out string file)
    {
        file = Path.Combine(Path.GetTempPath(), $"bfs-system-{Guid.NewGuid():N}.bfgd");
        using (FileStream output = File.Create(file))
            SystemAssetPreparer.Prepare(GameRomfs.Totk!, output, tiles, 0, actors, ["static"], effectTextures);
        return AssetPack.Open(file);
    }

    [TotkFact]
    public void TheSystemPackHoldsTheMasksTheBakeAndTheShaders()
    {
        using AssetPack pack = Prepare(["MainField_G_20_54"], ["Npc_Zelda_AncientHyrule", "Animal_Cow_A"], [], out string file);
        try
        {
            Assert.NotNull(pack.GameData("CloudMask/2"));
            byte[] index = pack.GameData(BakeImporter.IndexEntry)!;
            Assert.Equal("KRSB", Encoding.ASCII.GetString(index, 0, 4));
            Assert.NotNull(pack.GameData(BakeImporter.TileEntry("MainField_G_20_54")));
            Assert.Null(pack.GameData(BakeImporter.TileEntry("MainField_G_20_55")));

            // A cow has cloth: its model, its animation archives and its physics file come from its pack.
            var cow = ActorImporter.Read(pack, "Animal_Cow_A")!.Value;
            Assert.NotNull(cow.Item1);
            Assert.Contains("Cow.bphcl", cow.Item2);
            Assert.NotNull(pack.GameData(ActorImporter.PhysicsEntryName("Animal_Cow_A", "Cow.bphcl")));

            // The agl programs are decompiled with the immediate constants folded in; the sky program reads none of the unbound block.
            string compose = Encoding.UTF8.GetString(pack.GameData(AglShaderImporter.EntryName("agl_hdr_compose", "frag"))!);
            Assert.Contains("void main", compose);
            Assert.DoesNotContain("fp_c1.data[0].x", compose);
            Assert.NotNull(pack.GameData(AglShaderImporter.EntryName("agl_sky_postfx_sky_fog", "frag")));
            Assert.NotNull(pack.GameData(AglShaderImporter.EntryName("agl_miasma_renderer_compose_ENABLE_CORRECT0_ENABLE_TILE_MODE0_ENABLE_ALPHA_MODE0", "vert")));

            GrassShaderImporter.Stage blade = GrassShaderImporter.Read(pack, "Blade", "Fragment")!;
            Assert.Contains("void main", blade.Glsl);
            Assert.NotNull(pack.GameData(GrassShaderImporter.MaterialEntry));
            Assert.Equal(16, Encoding.ASCII.GetString(pack.GameData(GrassShaderImporter.ArchiveEntry)!).Length);

            Assert.Contains("void main", Encoding.UTF8.GetString(pack.GameData(TerrainWaterImporter.EntryName("frag"))!));
            TerrainWaterImporter.WaterTexture mask = TerrainWaterImporter.Read(pack, "WaterEmm")!;
            Assert.Equal("R8", mask.Format);
            Assert.Equal(mask.Width * mask.Height * mask.Layers, mask.Bytes.Length);

            Assert.NotNull(pack.GameData(EffectImporter.FileEntry("static")));
        }
        finally
        {
            pack.Dispose();
            File.Delete(file);
        }
    }

    [TotkFact]
    public void ASwitchOneSharcFileReadsItsStagesFromTheMaxwellLayout()
    {
        using AssetPack pack = Prepare([], [], [], out string file);
        try
        {
            // Every one of the 402 stages of the application package once failed: its code size is not where an Ampere control block keeps it.
            Assert.NotNull(pack.GameData(AglShaderImporter.EntryName("agl_hdr_compose_ENABLE_COLOR_CORRECTION_TABLE0", "vert")));
            Assert.NotNull(pack.GameData(AglShaderImporter.EntryName("agl_cloud", "frag")));
        }
        finally
        {
            pack.Dispose();
            File.Delete(file);
        }
    }
}
