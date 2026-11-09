namespace BfShaderSharp.Pack.Records;

/// <summary>A curve of keyed values over frames, as the model file holds it.</summary>
/// <remarks>
/// Flags is the curve flags word, which says the frame type, key type, curve type, wrap modes and whether the keys are
/// compressed. Scale, Offset and Delta dequantize narrow keys. Frames and exactly one of the four key arrays are filled,
/// according to the curve type: Keys for float curves, IntKeys and BoolKeys for steps, CompressedKeys for the compressed
/// cubic curves of Switch 2 files, whose bytes are kept as they are (HeaderWord is the word before the scale and
/// offset). DataOffset is the byte offset in the animated value that the curve drives.
/// </remarks>
public sealed record CurveRecord(
    ushort Flags,
    uint DataOffset,
    float StartFrame,
    float EndFrame,
    float Scale,
    float Offset,
    float Delta,
    uint HeaderWord,
    float[] Frames,
    float[] Keys,
    int[] IntKeys,
    byte[] BoolKeys,
    byte[] CompressedKeys)
{
    public void Write(RecordWriter writer)
    {
        writer.U16(Flags);
        writer.U32(DataOffset);
        writer.F32(StartFrame);
        writer.F32(EndFrame);
        writer.F32(Scale);
        writer.F32(Offset);
        writer.F32(Delta);
        writer.U32(HeaderWord);
        writer.List(Frames, (w, v) => w.F32(v));
        writer.List(Keys, (w, v) => w.F32(v));
        writer.List(IntKeys, (w, v) => w.I32(v));
        writer.Bytes(BoolKeys);
        writer.Bytes(CompressedKeys);
    }

    public static CurveRecord Read(RecordReader reader) =>
        new(reader.U16(), reader.U32(), reader.F32(), reader.F32(), reader.F32(), reader.F32(), reader.F32(), reader.U32(),
            [.. reader.List(4, r => r.F32())], [.. reader.List(4, r => r.F32())], [.. reader.List(4, r => r.I32())], reader.Bytes().ToArray(), reader.Bytes().ToArray());

    internal const int MinimumSize = 2 + 4 + 5 * 4 + 4 + 5 * 4;
}

/// <summary>The animation of one bone: its flags, the starting transform and the curves that change it.</summary>
/// <remarks>Base is ten floats (scale, rotation quaternion, translation) or empty when the animation stores no base values. The curves each drive a component named by their data offset.</remarks>
public sealed record BoneAnimRecord(string Name, uint Flags, byte BeginRotate, byte BeginTranslate, byte BeginBaseTranslate, int BeginCurve, float[] Base, IReadOnlyList<CurveRecord> Curves)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.U32(Flags);
        writer.U8(BeginRotate);
        writer.U8(BeginTranslate);
        writer.U8(BeginBaseTranslate);
        writer.I32(BeginCurve);
        writer.List(Base, (w, v) => w.F32(v));
        writer.List(Curves, (w, v) => v.Write(w));
    }

    public static BoneAnimRecord Read(RecordReader reader) =>
        new(reader.String(), reader.U32(), reader.U8(), reader.U8(), reader.U8(), reader.I32(), [.. reader.List(4, r => r.F32())], reader.List(CurveRecord.MinimumSize, CurveRecord.Read));
}

/// <summary>A skeletal animation: its bones' animations, bound to a skeleton by bone name.</summary>
/// <remarks>Flags holds the loop and baked bits and the scale and rotation modes of the model file.</remarks>
public sealed record SkeletalAnimRecord(string Name, uint Flags, int FrameCount, IReadOnlyList<BoneAnimRecord> Bones)
{
    public bool Loop => (Flags & 4) != 0;

    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.U32(Flags);
        writer.I32(FrameCount);
        writer.List(Bones, (w, v) => v.Write(w));
    }

    public static SkeletalAnimRecord Read(RecordReader reader) =>
        new(reader.String(), reader.U32(), reader.I32(), reader.List(21, BoneAnimRecord.Read));
}

/// <summary>How one parameter of a material is animated: the curves and constants that drive it.</summary>
/// <remarks>The parameter is named by the shader uniform it drives, and the curves' data offsets are byte offsets inside it. The curves and constants are ranges of the item's lists.</remarks>
public sealed record ParamAnimInfoRecord(string Name, ushort BeginCurve, ushort FloatCurveCount, ushort IntCurveCount, ushort BeginConstant, ushort ConstantCount, ushort SubBindIndex)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        foreach (ushort value in (ushort[])[BeginCurve, FloatCurveCount, IntCurveCount, BeginConstant, ConstantCount, SubBindIndex]) writer.U16(value);
    }

    public static ParamAnimInfoRecord Read(RecordReader reader) =>
        new(reader.String(), reader.U16(), reader.U16(), reader.U16(), reader.U16(), reader.U16(), reader.U16());
}

/// <summary>How one texture slot of a material is animated: the curve or constant that selects the texture.</summary>
public sealed record PatternAnimInfoRecord(string Name, short CurveIndex, ushort BeginConstant, sbyte SubBindIndex)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.U16((ushort)CurveIndex);
        writer.U16(BeginConstant);
        writer.U8((byte)SubBindIndex);
    }

    public static PatternAnimInfoRecord Read(RecordReader reader) =>
        new(reader.String(), (short)reader.U16(), reader.U16(), (sbyte)reader.U8());
}

/// <summary>A constant value that a material animation sets on the value at a byte offset.</summary>
public readonly record struct ConstantRecord(uint DataOffset, uint Value);

/// <summary>The animation of one material: what it does to its parameters and texture slots.</summary>
/// <remarks>BaseData is the item's list of base value offsets, as the file holds it.</remarks>
public sealed record MaterialAnimItemRecord(
    string Name,
    IReadOnlyList<ParamAnimInfoRecord> Params,
    IReadOnlyList<PatternAnimInfoRecord> Patterns,
    IReadOnlyList<CurveRecord> Curves,
    IReadOnlyList<ConstantRecord> Constants,
    IReadOnlyList<ushort> BaseData)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.List(Params, (w, v) => v.Write(w));
        writer.List(Patterns, (w, v) => v.Write(w));
        writer.List(Curves, (w, v) => v.Write(w));
        writer.List(Constants, (w, v) =>
        {
            w.U32(v.DataOffset);
            w.U32(v.Value);
        });
        writer.List(BaseData, (w, v) => w.U16(v));
    }

    public static MaterialAnimItemRecord Read(RecordReader reader) =>
        new(reader.String(), reader.List(14, ParamAnimInfoRecord.Read), reader.List(7, PatternAnimInfoRecord.Read),
            reader.List(CurveRecord.MinimumSize, CurveRecord.Read), reader.List(8, r => new ConstantRecord(r.U32(), r.U32())), reader.List(2, r => r.U16()));
}

/// <summary>A material animation of one kind, bound to materials by name.</summary>
/// <remarks>
/// Kind is 0 for a general material animation, 1 shader parameter, 2 colour, 3 texture transform and 4 texture pattern,
/// the section of the model file it came from. TextureNames are the textures a pattern animation switches between.
/// </remarks>
public sealed record MaterialAnimRecord(byte Kind, string Name, ushort Flags, int FrameCount, IReadOnlyList<MaterialAnimItemRecord> Items, IReadOnlyList<string> TextureNames)
{
    public bool Loop => (Flags & 4) != 0;

    public void Write(RecordWriter writer)
    {
        writer.U8(Kind);
        writer.String(Name);
        writer.U16(Flags);
        writer.I32(FrameCount);
        writer.List(Items, (w, v) => v.Write(w));
        writer.List(TextureNames, (w, v) => w.String(v));
    }

    public static MaterialAnimRecord Read(RecordReader reader) =>
        new(reader.U8(), reader.String(), reader.U16(), reader.I32(), reader.List(18, MaterialAnimItemRecord.Read), reader.List(2, r => r.String()));
}

/// <summary>The animation of visibility: one flag and one name per animated bone, or per material.</summary>
/// <remarks>Kind is 0 for bone visibility and 1 for material visibility. Base holds the starting visibility of each name.</remarks>
public sealed record VisibilityAnimRecord(byte Kind, string Name, ushort Flags, int FrameCount, IReadOnlyList<string> Names, byte[] Base, IReadOnlyList<CurveRecord> Curves)
{
    public bool Loop => (Flags & 4) != 0;

    public void Write(RecordWriter writer)
    {
        writer.U8(Kind);
        writer.String(Name);
        writer.U16(Flags);
        writer.I32(FrameCount);
        writer.List(Names, (w, v) => w.String(v));
        writer.Bytes(Base);
        writer.List(Curves, (w, v) => v.Write(w));
    }

    public static VisibilityAnimRecord Read(RecordReader reader) =>
        new(reader.U8(), reader.String(), reader.U16(), reader.I32(), reader.List(2, r => r.String()), reader.Bytes().ToArray(), reader.List(CurveRecord.MinimumSize, CurveRecord.Read));
}

/// <summary>How a key shape of a shape is weighted over time: its name and the index of its curve, -1 for none.</summary>
public readonly record struct KeyShapeInfoRecord(string Name, sbyte CurveIndex, sbyte SubBindIndex);

/// <summary>The animation of one shape's key shapes.</summary>
public sealed record VertexShapeAnimRecord(string Name, IReadOnlyList<KeyShapeInfoRecord> KeyShapes, IReadOnlyList<CurveRecord> Curves, float[] Base)
{
    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.List(KeyShapes, (w, v) =>
        {
            w.String(v.Name);
            w.U8((byte)v.CurveIndex);
            w.U8((byte)v.SubBindIndex);
        });
        writer.List(Curves, (w, v) => v.Write(w));
        writer.List(Base, (w, v) => w.F32(v));
    }

    public static VertexShapeAnimRecord Read(RecordReader reader) =>
        new(reader.String(), reader.List(4, r => new KeyShapeInfoRecord(r.String(), (sbyte)r.U8(), (sbyte)r.U8())), reader.List(CurveRecord.MinimumSize, CurveRecord.Read), [.. reader.List(4, r => r.F32())]);
}

/// <summary>A shape animation: the key shape weights of the shapes it animates, bound by shape name.</summary>
public sealed record ShapeAnimRecord(string Name, uint Flags, int FrameCount, IReadOnlyList<VertexShapeAnimRecord> Shapes)
{
    public bool Loop => (Flags & 4) != 0;

    public void Write(RecordWriter writer)
    {
        writer.String(Name);
        writer.U32(Flags);
        writer.I32(FrameCount);
        writer.List(Shapes, (w, v) => v.Write(w));
    }

    public static ShapeAnimRecord Read(RecordReader reader) =>
        new(reader.String(), reader.U32(), reader.I32(), reader.List(14, VertexShapeAnimRecord.Read));
}
