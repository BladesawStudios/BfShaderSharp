using System.Collections.Generic;

namespace Ryujinx.Graphics.Shader.StructuredIr
{
    class AstNode : IAstNode
    {
        private readonly int _hash = IntermediateRepresentation.NodeHash.Next();

        public override int GetHashCode() => _hash;

        public AstBlock Parent { get; set; }

        public LinkedListNode<IAstNode> LLNode { get; set; }
    }
}
