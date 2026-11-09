using BfShaderSharp.Errors;
using BfShaderSharp.Pack;

namespace BfShaderSharp.Tests.Pack;

public class PackRoundTripTests
{
    private static string Temp() => Path.Combine(Path.GetTempPath(), $"bfap-{Guid.NewGuid():N}.bfap");

    private static AssetPack Build(string path, Action<PackWriter> fill)
    {
        using (FileStream stream = File.Create(path))
        {
            PackWriter writer = new(stream);
            fill(writer);
            writer.Finish();
        }

        return AssetPack.Open(path);
    }

    [Fact]
    public void BlobsAreStoredOnceAndComeBackDecoded()
    {
        string path = Temp();
        byte[] big = Enumerable.Range(0, 5000).Select(i => (byte)(i % 7)).ToArray();
        BlobKey a = default, b = default, tiny = default;
        AssetPack pack = Build(path, w =>
        {
            a = w.Add(big);
            b = w.Add(big);
            tiny = w.Add([1, 2, 3], BlobCodec.Raw);
        });
        Assert.Equal(a, b);
        Assert.Equal(2, pack.BlobCount);
        Assert.Equal(big, pack.Bytes(a));
        Assert.Equal(new byte[] { 1, 2, 3 }, pack.Bytes(tiny));
        pack.Dispose();
        File.Delete(path);
    }

    [Fact]
    public void ZstdFramesAreKeptAsStoredWithTheirDictionary()
    {
        string path = Temp();
        byte[] frame = [0x28, 0xB5, 0x2F, 0xFD, 1, 2, 3, 4];
        BlobKey key = default;
        AssetPack pack = Build(path, w => key = w.AddFrame(frame, 99, 7));
        BlobData data = pack.Blob(key);
        Assert.Equal(frame, data.Bytes);
        Assert.Equal(BlobCodec.ZstdFrame, data.Codec);
        Assert.Equal(99, data.RawSize);
        Assert.Equal(7u, data.DictionaryId);
        pack.Dispose();
        File.Delete(path);
    }

    [Fact]
    public void EntriesAreListedByKindAndName()
    {
        string path = Temp();
        BlobKey key = default;
        AssetPack pack = Build(path, w =>
        {
            key = w.Add([9], BlobCodec.Raw);
            w.AddEntry(PackEntryKind.Texture, "b", key);
            w.AddEntry(PackEntryKind.Model, "z", key);
            w.AddEntry(PackEntryKind.Model, "a", key);
        });
        Assert.Equal(["a", "z", "b"], pack.Entries.Select(e => e.Name));
        Assert.Equal(key, pack.Find(PackEntryKind.Model, "z")!.Value.Key);
        Assert.Null(pack.Find(PackEntryKind.Texture, "z"));
        pack.Dispose();
        File.Delete(path);
    }

    [Fact]
    public void ManyThreadsAddingTheSameContentsStoreItOnce()
    {
        string path = Temp();
        byte[] data = Enumerable.Range(0, 4096).Select(i => (byte)(i * 31)).ToArray();
        AssetPack pack = Build(path, w => Parallel.For(0, 64, _ => w.Add(data)));
        Assert.Equal(1, pack.BlobCount);
        Assert.Equal(data, pack.Bytes(BlobKey.Of(data)));
        pack.Dispose();
        File.Delete(path);
    }

    [Fact]
    public void AMissingBlobOrBadHeaderIsAFormatError()
    {
        string path = Temp();
        using (AssetPack pack = Build(path, w => w.Add([1], BlobCodec.Raw))) Assert.Throws<BfShaderFormatException>(() => pack.Blob(new BlobKey(1, 2)));
        byte[] bytes = File.ReadAllBytes(path);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        Assert.Throws<BfShaderFormatException>(() => AssetPack.Open(path));
        File.Delete(path);
    }
}
