using BfresSharp.Model;
using BfresSharp.Model.Skeleton;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;

namespace BfShaderSharp.Assets;

/// <summary>Stores one model of a model file: its skeleton, shapes and materials as records, and the model record that lists them.</summary>
/// <remarks>
/// Each piece is stored under the hash of its encoding, so what models share (a skeleton, a material, a vertex stream) is
/// written once. Returns the key of the model record.
/// </remarks>
public static class ModelImporter
{
    public static BlobKey Import(AssetContext context, ResModel model)
    {
        BlobKey skeleton = Skeleton(context.Pack, model.Skeleton);
        List<MaterialRef> materials = [.. model.Materials.Select(m => new MaterialRef(m.Value.Name, MaterialImporter.Import(context, m.Value)))];
        List<ShapeRef> shapes = [.. model.Shapes.Select(s => new ShapeRef(s.Key, s.Value.MaterialIndex, s.Value.BoneIndex, ShapeImporter.Import(context.Pack, model, s.Value)))];
        RecordWriter writer = new();
        new ModelRecord(model.Name, model.Flags, model.TotalVertexCount, skeleton, shapes, materials).Write(writer);
        return context.Pack.Add(writer.ToArray(), BlobCodec.Raw);
    }

    private static BlobKey Skeleton(PackWriter pack, Skeleton skeleton)
    {
        RecordWriter writer = new();
        new SkeletonRecord(
            skeleton.RawFlags,
            [.. skeleton.Bones.Select(b => Bone(b.Value))],
            skeleton.MatrixToBoneList,
            [.. skeleton.InverseModelMatrices.Select(m => m.ToArray())]).Write(writer);
        return pack.Add(writer.ToArray(), BlobCodec.Raw);
    }

    private static BoneRecord Bone(Bone bone) =>
        new(bone.Name, bone.ParentIndex, bone.RawFlags, bone.SmoothMatrixIndex, bone.RigidMatrixIndex, bone.BillboardIndex,
            [bone.Scale.X, bone.Scale.Y, bone.Scale.Z], [bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W], [bone.Position.X, bone.Position.Y, bone.Position.Z]);
}
