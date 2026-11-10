using System;

namespace Ryujinx.Graphics.Shader
{
    /// <summary>Which of the two mesh pipeline stages a program is.</summary>
    public enum MeshProgramKind
    {
        None,
        Task,
        Mesh,
    }

    /// <summary>What one 32-element line of the vertex attribute region of a mesh program holds.</summary>
    public enum MeshLineKind
    {
        /// <summary>A value with no output of its own here (a per-primitive system value): written to the region and ignored.</summary>
        Unused,
        PositionX,
        PositionY,
        PositionZ,
        PositionW,
        PointSize,
        /// <summary>Component <see cref="MeshLine.Index"/> % 4 of generic attribute <see cref="MeshLine.Index"/> / 4.</summary>
        Generic,
    }

    public readonly record struct MeshLine(MeshLineKind Kind, int Index = 0);

    /// <summary>The sizes and outputs of a task or mesh program, which its registers set on the hardware, and the layout of its stage buffer in workgroup memory.</summary>
    /// <remarks>
    /// The stage buffer is emulated in workgroup memory, in words: the payload region, then the vertex attribute lines in groups of
    /// 32 vertices (one 32-word line per output), then the map (one word for each byte of vertex indices) and the primitive count.
    /// </remarks>
    public sealed class MeshProgramInfo
    {
        public const int GroupSize = 32;

        public MeshProgramKind Kind { get; init; }

        public int PayloadWords { get; init; } = 512;

        public int MaxVertices { get; init; }

        public int MaxPrimitives { get; init; }

        public MeshLine[] VertexLines { get; init; } = [];

        public int PayloadBase => 0;

        public int AttributeBase => PayloadWords;

        public int VertexGroups => Math.Max((MaxVertices + GroupSize - 1) / GroupSize, 1);

        public int AttributeWords => VertexGroups * Math.Max(VertexLines.Length, 1) * GroupSize;

        public int MapBase => AttributeBase + AttributeWords;

        public int MapWords => 4 + 3 * Math.Max(MaxPrimitives, 1) + 4;

        public int PrimitiveCountWord => MapBase + MapWords;

        public int SharedMemoryBytes => (PrimitiveCountWord + 1) * 4;
    }
}
