using Ryujinx.Graphics.Shader.StructuredIr;
using Ryujinx.Graphics.Shader.Translation;

namespace Ryujinx.Graphics.Shader.CodeGen.Glsl
{
    class CodeGenContext
    {
        public const string Tab = "    ";

        public StructuredFunction CurrentFunction { get; set; }

        public StructuredProgramInfo Info { get; }

        public AttributeUsage AttributeUsage { get; }
        public ShaderDefinitions Definitions { get; }
        public ShaderProperties Properties { get; }
        public HostCapabilities HostCapabilities { get; }
        public ILogger Logger { get; }
        public TargetApi TargetApi { get; }

        public OperandManager OperandManager { get; }

        private readonly Utf8Code _code;

        private int _level;

        public CodeGenContext(StructuredProgramInfo info, CodeGenParameters parameters)
        {
            Info = info;
            AttributeUsage = parameters.AttributeUsage;
            Definitions = parameters.Definitions;
            Properties = parameters.Properties;
            HostCapabilities = parameters.HostCapabilities;
            Logger = parameters.Logger;
            TargetApi = parameters.TargetApi;

            OperandManager = new OperandManager();

            _code = new Utf8Code();
        }

        public void AppendLine()
        {
            _code.AppendNewLine();
        }

        public void AppendLine(string str)
        {
            _code.AppendSpaces(_level * Tab.Length);
            _code.Append(str);
            _code.AppendNewLine();
        }

        public Utf8Code GetCode()
        {
            return _code;
        }

        public void EnterScope()
        {
            AppendLine("{");

            _level++;
        }

        public void LeaveScope(string suffix = "")
        {
            if (_level == 0)
            {
                return;
            }

            _level--;

            AppendLine("}" + suffix);
        }

        public StructuredFunction GetFunction(int id)
        {
            return Info.Functions[id];
        }

    }
}
