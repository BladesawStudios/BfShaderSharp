using Ryujinx.Graphics.Shader.Decoders;
using Ryujinx.Graphics.Shader.IntermediateRepresentation;
using Ryujinx.Graphics.Shader.Translation;

using static Ryujinx.Graphics.Shader.Instructions.InstEmitHelper;
using static Ryujinx.Graphics.Shader.IntermediateRepresentation.OperandHelper;

namespace Ryujinx.Graphics.Shader.Instructions
{
    static partial class InstEmit
    {
        // The stage buffer of a task or mesh program is workgroup memory here, laid out by MeshProgramInfo: the payload region
        // first, then the attribute lines of the vertices, the map of vertex indices and the primitive count.
        public static void IsbeNx2(EmitterContext context)
        {
            InstIsbeNx2 op = context.GetOp<InstIsbeNx2>();
            MeshProgramInfo mesh = context.TranslatorContext.Definitions.Mesh;

            if (mesh == null)
            {
                context.TranslatorContext.GpuAccessor.Log("Stage buffer access outside task and mesh programs.");
                return;
            }

            Operand address = context.IAdd(GetSrcReg(context, op.SrcA), Const(op.Offset));
            int id = context.ResourceManager.SharedMemoryId;

            if (!op.Write)
            {
                // Input reads see the payload region: word 0 holds the workgroup index, the rest is the task's output. Output reads see
                // back what the program wrote, which is the payload region for a task and the attribute lines for a mesh program.
                int regionBase = op.Output && mesh.Kind == MeshProgramKind.Mesh ? mesh.AttributeBase : mesh.PayloadBase;
                Operand word = context.ShiftRightU32(address, Const(2));
                context.Copy(GetDest(op.Data), context.Load(StorageKind.SharedMemory, id, context.IAdd(word, Const(regionBase))));
                return;
            }

            Operand value = GetSrcReg(context, op.Data);

            if (mesh.Kind == MeshProgramKind.Task)
            {
                context.Store(StorageKind.SharedMemory, id, context.ShiftRightU32(address, Const(2)), value);
            }
            else if (op.Attribute)
            {
                if (!op.Is32)
                {
                    context.TranslatorContext.GpuAccessor.Log("U8 stage buffer attribute write.");
                    return;
                }

                Operand word = context.IAdd(context.ShiftRightU32(address, Const(2)), Const(mesh.AttributeBase));
                context.Store(StorageKind.SharedMemory, id, context.IMinimumU32(word, Const(mesh.MapBase - 1)), value);
            }
            else if (op.Is32)
            {
                // The primitive count: the map is written from its end, so its first word is at byte 3.
                if (address.Type != OperandType.Constant || address.Value != 3)
                {
                    context.TranslatorContext.GpuAccessor.Log("32-bit stage buffer map write.");
                    return;
                }

                context.Store(StorageKind.SharedMemory, id, Const(mesh.PrimitiveCountWord), value);
            }
            else
            {
                Operand word = context.IAdd(context.IMinimumU32(address, Const(mesh.MapWords - 1)), Const(mesh.MapBase));
                context.Store(StorageKind.SharedMemory, id, word, context.BitwiseAnd(value, Const(0xff)));
            }
        }
    }
}
