using System;

namespace Ryujinx.Graphics.Shader.IntermediateRepresentation
{
    /// <summary>
    /// Cheap hash codes for reference-keyed collections.
    /// The default object hash code is far slower, and the collections do not depend on its value
    /// (enumeration order follows insertion order), so a per-thread counter is enough.
    /// </summary>
    static class NodeHash
    {
        [ThreadStatic]
        private static int _next;

        public static int Next()
        {
            return ++_next;
        }
    }
}
