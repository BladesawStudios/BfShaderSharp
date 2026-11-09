using BfShaderSharp.Assets;

namespace BfShaderSharp.Tests.Assets;

public sealed class RomfsFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"romfs-{Guid.NewGuid():N}");

    public RomfsFilesTests()
    {
        foreach (string file in new[] { "Model/Link.Link.bfres", "Model/Land_A.Land_A_01.bfres.zs", "Model/Land_B.Land_B_01.bfres.zs", "Tex/Tex_A.bntx", "Tex/Tex_B.bntx.zs", "Shader/Model.bfsha" })
        {
            string path = Path.Combine(_root, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1, 2, 3]);
        }
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void ModelFilesAreSelectedByTheNameBeforeTheFirstDot()
    {
        using RomfsFiles romfs = new(_root);
        Assert.Equal(3, romfs.ModelFiles([]).Count);
        Assert.Equal(["Land_A.Land_A_01.bfres.zs", "Land_B.Land_B_01.bfres.zs"], romfs.ModelFiles(["Land_*"]).Select(Path.GetFileName));
        Assert.Equal(["Link.Link.bfres"], romfs.ModelFiles(["link"]).Select(Path.GetFileName));
        Assert.Empty(romfs.ModelFiles(["Nothing"]));
    }

    [Fact]
    public void TexturesAreFoundByNameWithOrWithoutCompression()
    {
        using RomfsFiles romfs = new(_root);
        Assert.EndsWith("Tex_A.bntx", romfs.TextureFile("Tex_A"));
        Assert.EndsWith("Tex_B.bntx.zs", romfs.TextureFile("Tex_B"));
        Assert.Null(romfs.TextureFile("Tex_C"));
        Assert.Equal(2, romfs.TextureFiles().Count);
        Assert.Single(romfs.ShaderFiles());
    }

    [Fact]
    public void StemIsTheNameUpToTheFirstDot()
    {
        Assert.Equal("Land_A", RomfsFiles.StemOf("x/Model/Land_A.Land_A_01.bfres.zs"));
        Assert.Equal("plain", RomfsFiles.StemOf("plain"));
    }

    [Fact]
    public void ReadReturnsUncompressedFilesAsTheyAreAndAGameWithoutAStringTableHasNoNames()
    {
        using RomfsFiles romfs = new(_root);
        Assert.Equal(new byte[] { 1, 2, 3 }, romfs.Read(Path.Combine(_root, "Model", "Link.Link.bfres")));
        Assert.Empty(romfs.ExternalNames());
    }
}
