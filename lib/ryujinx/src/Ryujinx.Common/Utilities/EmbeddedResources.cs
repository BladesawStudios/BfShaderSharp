using System;
using System.IO;
using System.Reflection;

namespace Ryujinx.Common
{
    // Trimmed copy: only reads text from the assembly named by the first path segment, which is all the shader translator needs.
    public static class EmbeddedResources
    {
        public static string ReadAllText(string filename)
        {
            string[] segments = filename.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (segments.Length < 2 || assembly.GetName().Name != segments[0])
                {
                    continue;
                }

                using Stream stream = assembly.GetManifestResourceStream(segments[0] + "." + segments[1].Replace('/', '.'));
                if (stream == null)
                {
                    return null;
                }

                using StreamReader reader = new(stream);
                return reader.ReadToEnd();
            }

            return null;
        }
    }
}
