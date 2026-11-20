using Yaz0Sharp;
using ZsDicSharp;

namespace BfShaderSharp.Assets;

/// <summary>Reads a game file as plain bytes whichever way it is stored: as it is, in zstd with the game's dictionaries (<c>.zs</c>), in the mesh codec (<c>.mc</c>, which needs no dictionary) or in Yaz0 (<c>.sbfres</c> and the other files of Breath of the Wild).</summary>
public static class GameFile
{
    /// <summary>True when the data is a mesh codec package, which is how a <c>.mc</c> file starts.</summary>
    public static bool IsMeshCodec(ReadOnlySpan<byte> data) => McSharp.MeshCodec.TryReadPackageHeader(data, out _);

    /// <summary>True when the data is stored in either compression.</summary>
    public static bool IsCompressed(ReadOnlySpan<byte> data) => IsMeshCodec(data) || Yaz0.IsCompressed(data) || ZsDic.IsCompressed(data);

    /// <summary>The plain bytes of the data read from <paramref name="path"/>. Zstd data needs dictionaries; without them it is returned as it is.</summary>
    public static byte[] Decode(byte[] data, string path, ZsDic? dictionaries)
    {
        if (Yaz0.IsCompressed(data)) return Yaz0.Decompress(data);
        if (IsMeshCodec(data))
            return McSharp.MeshCodec.DecompressMc(data, out McSharp.McStatus status) ?? throw new InvalidDataException($"{Path.GetFileName(path)} could not be unpacked from the mesh codec ({status}).");
        return ZsDic.IsCompressed(data) && dictionaries is not null ? dictionaries.Decompress(data, path) : data;
    }

    /// <summary>The file as plain bytes. Zstd files are opened with the dictionaries of the nearest ancestor romfs that has them.</summary>
    public static byte[] Read(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (Yaz0.IsCompressed(data)) return Yaz0.Decompress(data);
        if (IsMeshCodec(data)) return Decode(data, path, null);
        if (!ZsDic.IsCompressed(data)) return data;
        for (DirectoryInfo? dir = new(Path.GetDirectoryName(Path.GetFullPath(path))!); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "Pack", "ZsDic.pack.zs"))) continue;
            using ZsDic dictionaries = ZsDic.FromRomfs(dir.FullName);
            return dictionaries.Decompress(data, path);
        }

        throw new InvalidOperationException("The file is compressed and no ancestor folder has Pack/ZsDic.pack.zs");
    }
}
