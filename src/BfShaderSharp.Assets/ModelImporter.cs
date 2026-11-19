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
        SelectPassPrograms(context, model);
        RecordWriter writer = new();
        new ModelRecord(model.Name, model.Flags, model.TotalVertexCount, skeleton, shapes, materials).Write(writer);
        return context.Pack.Add(writer.ToArray(), BlobCodec.Raw);
    }

    /// <summary>The draw-time options of a shape in one pass: its skin weight count and the pass.</summary>
    public static Dictionary<string, string> PassOptions(int skinCount, string assignType) =>
        new() { ["gsys_weight"] = skinCount.ToString(System.Globalization.CultureInfo.InvariantCulture), ["gsys_assign_type"] = assignType };

    // The program of every pass for every shape, so the prepared shader file holds what a renderer draws each shape with.
    private static void SelectPassPrograms(AssetContext context, ResModel model)
    {
        if (context.AssignTypes.Count == 0) return;
        foreach (var shape in model.Shapes.Select(s => s.Value))
        {
            if (shape.MaterialIndex >= model.Materials.Count) continue;
            var material = model.Materials[shape.MaterialIndex].Value;
            foreach (string pass in context.AssignTypes)
            {
                int program = context.Shaders.Select(material, PassOptions(shape.VertexSkinCount, pass));
                context.Programs.Add(material.ShaderAssign.ShaderArchiveName, material.ShaderAssign.ShadingModelName, program);
            }
        }
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
