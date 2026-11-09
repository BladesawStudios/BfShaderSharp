using BfresSharp.Model;
using BfresSharp.Model.Geometry;
using BfShaderSharp.Errors;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;

namespace BfShaderSharp.Assets;

/// <summary>Turns a shape and its vertex buffer into a geometry record and stores the vertex and index bytes.</summary>
/// <remarks>
/// Vertex streams and index buffers are blobs of their own, Brotli-compressed, so a stream that two shapes or two models
/// share is stored once. The bytes are kept as the model file has them. A shape whose vertex buffer index is outside the
/// model is a format error.
/// </remarks>
public static class ShapeImporter
{
    public static BlobKey Import(PackWriter pack, ResModel model, Shape shape)
    {
        if (shape.VertexBufferIndex >= model.VertexBuffers.Count) throw new BfShaderFormatException($"Shape {shape.Name} uses vertex buffer {shape.VertexBufferIndex} of {model.VertexBuffers.Count}");
        VertexBuffer buffer = model.VertexBuffers[shape.VertexBufferIndex];
        RecordWriter writer = new();
        new ShapeRecord(
            buffer.VertexCount,
            shape.VertexSkinCount,
            [.. buffer.Attributes.Select(a => new AttribRecord(a.Value.Name, (uint)a.Value.Format, a.Value.Offset, a.Value.BufferIndex))],
            [.. buffer.Streams.Select(s => new StreamRecord(s.Stride, pack.Add(s.Data.Span)))],
            [.. shape.Meshes.Select(m => Mesh(pack, m))],
            shape.Radii,
            [.. shape.BoundingRadii.SelectMany(v => new[] { v.X, v.Y, v.Z, v.W })],
            shape.SkinBoneIndices).Write(writer);
        return pack.Add(writer.ToArray(), BlobCodec.Raw);
    }

    private static MeshRecord Mesh(PackWriter pack, Mesh mesh) =>
        new((uint)mesh.PrimitiveType, (uint)mesh.IndexFormat, mesh.FirstVertex, [.. mesh.SubMeshes.Select(s => (s.Offset, s.Count))], pack.Add(mesh.IndexData.Span), (uint)mesh.IndexCount);
}
