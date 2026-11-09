namespace BfShaderSharp.Pack.Records;

/// <summary>A material as a renderer needs it: the shader program it runs, its options, samplers, parameters and render state.</summary>
/// <remarks>
/// The record holds no name and no model-specific index, so two materials with the same program, values and textures
/// encode to the same bytes and a pack stores them once. Program is the program number of the shading model in the shader
/// archive named by ShaderArchive and ShadingModel, or -1 when the archive was not available when the pack was made.
/// Choices are the resolved option choices the program was picked for. Parameter data is the raw value bytes that the
/// parameters index into. RenderStateMode and BlendMode are -1 when the material has no render state. Block is the
/// material's uniform block, ready to upload, and Layout the record of where each named uniform sits in it (a
/// <see cref="UniformLayoutRecord"/>, shared by every material of the shading model); both are the default key when the
/// shading model was not available or declares no material block.
/// </remarks>
public sealed record MaterialRecord(
    string ShaderArchive,
    string ShadingModel,
    int Program,
    uint Flags,
    IReadOnlyList<NamePair> Choices,
    IReadOnlyList<RenderInfoRecord> RenderInfos,
    IReadOnlyList<SamplerRecord> Samplers,
    IReadOnlyList<NamePair> SamplerAssigns,
    IReadOnlyList<ParamRecord> Params,
    ReadOnlyMemory<byte> ParamData,
    int RenderStateMode,
    int BlendMode,
    BlobKey Layout = default,
    BlobKey Block = default)
{
    public void Write(RecordWriter writer)
    {
        writer.String(ShaderArchive);
        writer.String(ShadingModel);
        writer.I32(Program);
        writer.U32(Flags);
        writer.List(Choices, (w, v) => v.Write(w));
        writer.List(RenderInfos, (w, v) => v.Write(w));
        writer.List(Samplers, (w, v) => v.Write(w));
        writer.List(SamplerAssigns, (w, v) => v.Write(w));
        writer.List(Params, (w, v) => v.Write(w));
        writer.Bytes(ParamData.Span);
        writer.I32(RenderStateMode);
        writer.I32(BlendMode);
        writer.Key(Layout);
        writer.Key(Block);
    }

    public static MaterialRecord Read(RecordReader reader) =>
        new(reader.String(), reader.String(), reader.I32(), reader.U32(),
            reader.List(4, NamePair.Read), reader.List(11, RenderInfoRecord.Read), reader.List(27, SamplerRecord.Read),
            reader.List(4, NamePair.Read), reader.List(5, ParamRecord.Read), reader.Bytes(), reader.I32(), reader.I32(), reader.Key(), reader.Key());
}
