using Ryujinx.Graphics.Shader.Translation;
using System;

namespace Ryujinx.Graphics.Shader
{
    public class ShaderProgram
    {
        public ShaderProgramInfo Info { get; }
        public TargetLanguage Language { get; }

        private string _code;

        /// <summary>The generated GLSL as UTF-8, when the program was generated as GLSL and its text was not replaced by <see cref="Prepend"/>.</summary>
        public Utf8Code CodeUtf8 { get; private set; }

        public string Code => _code ??= CodeUtf8?.ToString();
        public byte[] BinaryCode { get; }

        private ShaderProgram(ShaderProgramInfo info, TargetLanguage language)
        {
            Info = info;
            Language = language;
        }

        public ShaderProgram(ShaderProgramInfo info, TargetLanguage language, string code) : this(info, language)
        {
            _code = code;
        }

        public ShaderProgram(ShaderProgramInfo info, TargetLanguage language, Utf8Code code) : this(info, language)
        {
            CodeUtf8 = code;
        }

        public ShaderProgram(ShaderProgramInfo info, TargetLanguage language, byte[] binaryCode) : this(info, language)
        {
            BinaryCode = binaryCode;
        }

        public void Prepend(string line)
        {
            _code = line + Environment.NewLine + Code;
            CodeUtf8 = null;
        }
    }
}
