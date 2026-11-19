using BntxSharp;
using BfShaderSharp.Pack;

namespace BfShaderSharp.Assets;

/// <summary>Reads the three 512x512 masks the cloud dome samples out of the game scene's environment archive and stores each as one byte per texel under <c>CloudMask/0</c> to <c>CloudMask/2</c>.</summary>
/// <remarks>The masks are BNTX textures inside the collect.genvres file of Env/GameScene.Nin_NX_NVN.genvb.zs, which starts its BNTX at 0x1000; only the red channel is kept. A game without that archive has no masks and imports none.</remarks>
public static class CloudMaskImporter
{
    public const int Size = 512;
    public const string Group = "CloudMask";
    private const string Archive = "Env/GameScene.Nin_NX_NVN.genvb.zs";
    private const int BntxOffset = 0x1000;
    private static readonly string[] SlotTextures = ["cloudtexture03", "cloudtexture02", "cloudtexture04"];

    public static string EntryName(int slot) => $"{Group}/{slot}";

    /// <summary>Stores the masks in the pack and returns how many were stored.</summary>
    public static int Import(AssetContext context)
    {
        int slot = 0;
        foreach (byte[] mask in Masks(context.Romfs))
            context.Pack.AddEntry(PackEntryKind.GameData, EntryName(slot++), context.Pack.Add(mask));
        return slot;
    }

    public static IEnumerable<byte[]> Masks(RomfsFiles romfs)
    {
        string path = Path.Combine(romfs.Root, "Env", "GameScene.Nin_NX_NVN.genvb.zs");
        if (!File.Exists(path)) yield break;
        byte[] collect = Collect(romfs.Read(path));
        BntxFile bntx = BntxFile.Load(collect.AsSpan(BntxOffset));
        foreach (string name in SlotTextures)
        {
            BntxTexture texture = bntx.Textures.FirstOrDefault(t => t.Name == name) ?? throw new InvalidDataException($"collect.genvres has no texture '{name}'.");
            if (texture.Width != Size || texture.Height != Size) throw new InvalidDataException($"'{name}' is {texture.Width}x{texture.Height}, expected {Size}x{Size}.");
            byte[] rgba = texture.ToRgba8();
            byte[] red = new byte[Size * Size];
            for (int i = 0; i < red.Length; i++) red[i] = rgba[i * 4];
            yield return red;
        }
    }

    private static byte[] Collect(byte[] archive)
    {
        var sarc = SarcSharp.SarcFile.FromBinary(archive);
        var entry = sarc.Entries.FirstOrDefault(e => e.Name.EndsWith("collect.genvres", StringComparison.Ordinal)) ?? throw new FileNotFoundException($"'{Archive}' has no collect.genvres entry.");
        return entry.Data;
    }
}
