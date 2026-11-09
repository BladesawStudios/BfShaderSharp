using System.Globalization;
using BfresSharp.Model;
using BfresSharp.Model.Animation;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;

namespace BfShaderSharp.Assets;

/// <summary>Imports the animations of one model file and names each in the pack.</summary>
/// <remarks>
/// Skeletal, material (general, shader parameter, colour, texture transform and texture pattern), visibility (bone and
/// material) and shape animations are each stored as one record named by the file's stem, a slash and the animation's name.
/// They bind by bone, material or shape name, not by index, so any model with those names can play them. Curves are kept
/// as the file holds them, compressed keys included. Hashed names of a Switch 2 file are resolved through the name table.
/// Returns the number of animations stored.
/// </remarks>
public static class AnimationImporter
{
    public static int Import(AssetContext context, string stem, ResFile file)
    {
        int count = 0;
        HashSet<string> names = [];
        foreach (SkeletalAnim animation in file.SkeletalAnims.Values) count += Store(context, PackEntryKind.SkeletalAnim, names, stem, animation.Name, Skeletal(animation).Write);
        (byte Kind, ResDict<MaterialAnim> Anims)[] materialAnims = [(0, file.MaterialAnims), (1, file.ShaderParamAnims), (2, file.ColorAnims), (3, file.TexSrtAnims), (4, file.TexPatternAnims)];
        foreach ((byte kind, ResDict<MaterialAnim> anims) in materialAnims)
        foreach (MaterialAnim animation in anims.Values) count += Store(context, PackEntryKind.MaterialAnim, names, stem, animation.Name, Material(context, kind, animation).Write);
        foreach (VisibilityAnim animation in file.VisibilityAnims.Values) count += Store(context, PackEntryKind.VisibilityAnim, names, stem, animation.Name, Visibility(0, animation).Write);
        foreach (VisibilityAnim animation in file.MatVisibilityAnims.Values) count += Store(context, PackEntryKind.VisibilityAnim, names, stem, animation.Name, Visibility(1, animation).Write);
        foreach (ShapeAnim animation in file.ShapeAnims.Values) count += Store(context, PackEntryKind.ShapeAnim, names, stem, animation.Name, Shape(animation).Write);
        return count;
    }

    private static int Store(AssetContext context, PackEntryKind kind, HashSet<string> taken, string stem, string name, Action<RecordWriter> write)
    {
        RecordWriter writer = new();
        write(writer);
        string entry = $"{stem}/{name}";
        for (int suffix = 2; !taken.Add($"{kind}:{entry}"); suffix++) entry = $"{stem}/{name}#{suffix}";
        context.Pack.AddEntry(kind, entry, context.Pack.Add(writer.ToArray()));
        return 1;
    }

    private static SkeletalAnimRecord Skeletal(SkeletalAnim animation) =>
        new(animation.Name, animation.Flags, animation.FrameCount, [.. animation.BoneAnims.Select(Bone)]);

    private static BoneAnimRecord Bone(BoneAnim bone) =>
        new(bone.Name, bone.Flags, bone.BeginRotate, bone.BeginTranslate, bone.BeginBaseTranslate, bone.BeginCurve,
            bone.BaseData is { } data ? [data.Scale.X, data.Scale.Y, data.Scale.Z, data.Rotate.X, data.Rotate.Y, data.Rotate.Z, data.Rotate.W, data.Translate.X, data.Translate.Y, data.Translate.Z] : [],
            [.. bone.Curves.Select(Curve)]);

    private static MaterialAnimRecord Material(AssetContext context, byte kind, MaterialAnim animation) =>
        new(kind, animation.Name, animation.Flags, animation.FrameCount, [.. animation.Items.Select(item => Item(context, item))], [.. animation.TextureNames]);

    private static MaterialAnimItemRecord Item(AssetContext context, MaterialAnimItem item) =>
        new(Name(context, item.Name, item.NameHash),
            [.. item.ParamAnimInfos.Select(i => new ParamAnimInfoRecord(Name(context, i.Name, i.NameHash), i.BeginCurve, i.FloatCurveCount, i.IntCurveCount, i.BeginConstant, i.ConstantCount, i.SubBindIndex))],
            [.. item.PatternAnimInfos.Select(i => new PatternAnimInfoRecord(Name(context, i.Name, i.NameHash), i.CurveIndex, i.BeginConstant, i.SubBindIndex))],
            [.. item.Curves.Select(Curve)],
            [.. item.Constants.Select(c => new ConstantRecord(c.AnimDataOffset, c.Value))],
            item.BaseDataList);

    private static VisibilityAnimRecord Visibility(byte kind, VisibilityAnim animation) =>
        new(kind, animation.Name, animation.Flags, animation.FrameCount, [.. animation.Names], [.. animation.BaseData.Select(b => (byte)(b ? 1 : 0))], [.. animation.Curves.Select(Curve)]);

    private static ShapeAnimRecord Shape(ShapeAnim animation) =>
        new(animation.Name, animation.Flags, animation.FrameCount,
            [.. animation.VertexShapeAnims.Select(v => new VertexShapeAnimRecord(v.Name, [.. v.KeyShapeAnimInfos.Select(k => new KeyShapeInfoRecord(k.Name, k.CurveIndex, k.SubBindIndex))], [.. v.Curves.Select(Curve)], v.BaseData))]);

    private static CurveRecord Curve(AnimCurve curve) =>
        new(curve.Flags, curve.AnimDataOffset, curve.StartFrame, curve.EndFrame, curve.Scale, curve.Offset, curve.Delta, curve.CompressedHeaderWord,
            curve.Frames, curve.Keys, curve.IntKeys, [.. curve.BoolKeys.Select(b => (byte)(b ? 1 : 0))], curve.CompressedKeyData);

    // A name the file left out is named by its hash, which the game's string table may know.
    private static string Name(AssetContext context, string name, ulong hash) =>
        name != "" || hash == 0 ? name : context.Names.TryGetValue(hash, out string? resolved) ? resolved : $"0x{hash.ToString("x16", CultureInfo.InvariantCulture)}";
}
