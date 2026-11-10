using System.Collections.Generic;
using System.Linq;

namespace Ryujinx.Graphics.Shader.CodeGen.Glsl
{
    /// <summary>Writes the parts of a task or mesh program's GLSL that are not its translated body: the interface and the entry point.</summary>
    /// <remarks>
    /// The translated body runs against a stage buffer kept in workgroup memory (see <see cref="MeshProgramInfo"/>). The entry point
    /// fills that memory from the task payload, runs the body, and hands the results to the mesh pipeline: a task program emits
    /// its workgroup count and payload, a mesh program sets its output counts and copies the vertex lines and primitive map
    /// out. Every invocation reaches the end of the entry point because the body is a function.
    /// </remarks>
    static class MeshProgramGenerator
    {
        public const string BodyName = "mesh_body";
        private const string Memory = "shared_memory";
        private const string Payload = "task_payload";
        private const string Components = "xyzw";

        public static void DeclareInterface(CodeGenContext context, MeshProgramInfo mesh)
        {
            context.AppendLine($"taskPayloadSharedEXT uint {Payload}[{mesh.PayloadWords}];");

            if (mesh.Kind == MeshProgramKind.Mesh)
            {
                context.AppendLine($"layout (triangles, max_vertices = {mesh.MaxVertices}, max_primitives = {mesh.MaxPrimitives}) out;");

                foreach (int generic in Generics(mesh))
                {
                    context.AppendLine($"layout (location = {generic}) out vec4 {DefaultNames.OAttributePrefix}{generic}[{mesh.MaxVertices}];");
                }
            }

            context.AppendLine();
        }

        public static void PrintEntryPoint(CodeGenContext context, MeshProgramInfo mesh)
        {
            context.AppendLine("void main()");
            context.EnterScope();
            context.AppendLine("uint lane = gl_LocalInvocationID.x;");
            context.AppendLine("uint threads = gl_WorkGroupSize.x;");

            if (mesh.Kind == MeshProgramKind.Task)
            {
                PrintTask(context, mesh);
            }
            else
            {
                PrintMesh(context, mesh);
            }

            context.LeaveScope();
        }

        private static void PrintTask(CodeGenContext context, MeshProgramInfo mesh)
        {
            context.AppendLine($"for (uint i = lane; i < {mesh.PayloadWords}u; i += threads) {Memory}[i] = 0u;");
            context.AppendLine($"{Memory}[0] = gl_WorkGroupID.x;");
            context.AppendLine("barrier();");
            context.AppendLine($"{BodyName}();");
            context.AppendLine("barrier();");
            context.AppendLine($"for (uint i = lane; i < {mesh.PayloadWords}u; i += threads) {Payload}[i] = {Memory}[i];");
            context.AppendLine($"uint count = {Memory}[1];");
            context.AppendLine("barrier();");
            context.AppendLine("EmitMeshTasksEXT(count, 1u, 1u);");
        }

        private static void PrintMesh(CodeGenContext context, MeshProgramInfo mesh)
        {
            context.AppendLine($"for (uint i = lane; i < {mesh.PayloadWords}u; i += threads) {Memory}[i] = {Payload}[i];");
            context.AppendLine($"{Memory}[0] = gl_WorkGroupID.x;");
            context.AppendLine($"{Memory}[{mesh.PrimitiveCountWord}] = 0u;");
            context.AppendLine("barrier();");
            context.AppendLine($"{BodyName}();");
            context.AppendLine("barrier();");
            context.AppendLine($"uint count = min({Memory}[{mesh.PrimitiveCountWord}], {mesh.MaxPrimitives}u);");
            context.AppendLine($"SetMeshOutputsEXT({mesh.MaxVertices}u, count);");

            PrintVertices(context, mesh);

            context.AppendLine("for (uint p = lane; p < count; p += threads)");
            context.EnterScope();
            context.AppendLine($"uint at = {mesh.MapBase}u + 4u + 3u * p;");
            context.AppendLine($"gl_PrimitiveTriangleIndicesEXT[p] = uvec3({Memory}[at], {Memory}[at + 1u], {Memory}[at + 2u]);");
            context.LeaveScope();
        }

        private static void PrintVertices(CodeGenContext context, MeshProgramInfo mesh)
        {
            int lines = mesh.VertexLines.Length;

            context.AppendLine($"for (uint v = lane; v < {mesh.MaxVertices}u; v += threads)");
            context.EnterScope();
            context.AppendLine($"uint first = {mesh.AttributeBase}u + (v >> 5u) * {lines * MeshProgramInfo.GroupSize}u + (v & 31u);");
            context.AppendLine("vec4 position = vec4(0.0, 0.0, 0.0, 1.0);");

            foreach (int generic in Generics(mesh))
            {
                context.AppendLine($"{DefaultNames.OAttributePrefix}{generic}[v] = vec4(0.0, 0.0, 0.0, 1.0);");
            }

            for (int line = 0; line < lines; line++)
            {
                MeshLine entry = mesh.VertexLines[line];
                string value = $"uintBitsToFloat({Memory}[first + {line * MeshProgramInfo.GroupSize}u])";

                switch (entry.Kind)
                {
                    case MeshLineKind.PositionX:
                    case MeshLineKind.PositionY:
                    case MeshLineKind.PositionZ:
                    case MeshLineKind.PositionW:
                        context.AppendLine($"position.{Components[entry.Kind - MeshLineKind.PositionX]} = {value};");
                        break;
                    case MeshLineKind.PointSize:
                        context.AppendLine($"gl_MeshVerticesEXT[v].gl_PointSize = {value};");
                        break;
                    case MeshLineKind.Generic:
                        context.AppendLine($"{DefaultNames.OAttributePrefix}{entry.Index / 4}[v].{Components[entry.Index % 4]} = {value};");
                        break;
                }
            }

            context.AppendLine("gl_MeshVerticesEXT[v].gl_Position = position;");
            context.LeaveScope();
        }

        private static IEnumerable<int> Generics(MeshProgramInfo mesh)
        {
            return mesh.VertexLines.Where(l => l.Kind == MeshLineKind.Generic).Select(l => l.Index / 4).Distinct().OrderBy(g => g);
        }
    }
}
