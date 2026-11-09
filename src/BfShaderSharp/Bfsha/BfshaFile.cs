using BfShaderSharp.Binary;
using BfShaderSharp.Container;
using BfShaderSharp.Errors;

namespace BfShaderSharp.Bfsha;

/// <summary>A BFSHA shader archive: a named set of shading models, each with its options, programs and compiled shaders.</summary>
/// <remarks>
/// Reading parses the header and the model table position. Each <see cref="ShaderModel"/> is created on demand and
/// reads its parts when asked, so opening a 300 MB archive costs a few header reads. Only version 9 files are read.
/// </remarks>
public sealed class BfshaFile
{
    private const string Magic = "FSHA";
    private readonly ByteView _view;
    private readonly ResDict _models;
    private readonly long _modelValues;

    private BfshaFile(ByteView view, ResHeader header)
    {
        _view = view;
        Header = header;
        Name = view.String((long)view.U64(0x38));
        Path = view.String((long)view.U64(0x40));
        _modelValues = (long)view.U64(0x48);
        _models = new ResDict(view, (long)view.U64(0x50));
        ModelCount = view.U16(0x78);
        if (ModelCount != _models.Count) throw new BfShaderFormatException($"Archive claims {ModelCount} models but its dictionary has {_models.Count}", 0x78);
        if (_modelValues + (long)ModelCount * ShaderModel.Size > view.Length) throw new BfShaderFormatException("Model array runs past the file", _modelValues);
    }

    public ResHeader Header { get; }

    public string Name { get; }

    public string Path { get; }

    public int ModelCount { get; }

    public static BfshaFile Read(ReadOnlyMemory<byte> bytes)
    {
        ByteView view = new(bytes);
        ResHeader header = ResHeader.Read(view, Magic);
        if (header.VersionMajor != 9) throw new BfShaderUnsupportedException($"BFSHA version {header.Version} is not supported; only version 9 is read", 8);
        return new BfshaFile(view, header);
    }

    public string ModelName(int index) => _models.Key(index);

    public ShaderModel Model(int index)
    {
        if ((uint)index >= (uint)ModelCount) throw new ArgumentOutOfRangeException(nameof(index));
        return new ShaderModel(_view, _modelValues + (long)index * ShaderModel.Size, ModelName(index));
    }

    public ShaderModel? Model(string name) => _models.IndexOf(name) is var i and >= 0 ? Model(i) : null;

    public IEnumerable<ShaderModel> Models()
    {
        for (int i = 0; i < ModelCount; i++) yield return Model(i);
    }
}
