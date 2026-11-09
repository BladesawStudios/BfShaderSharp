# Ryujinx shader translator (prebuilt)

`Ryujinx.Graphics.Shader.dll`, `Ryujinx.Common.dll` and `Spv.Generator.dll` are the Maxwell shader translator of Ryujinx (MIT, see
`Ryujinx License.txt`), copied unchanged from the ShaderLibrary `CompileTool/Libs` folder. They translate the Maxwell code that
`BfShaderSharp.Decompile` produces from Switch 2 Ampere code. They are binaries of unrecorded provenance; replace them with a build
from source (a submodule of the Ryujinx `Ryujinx.Graphics.Shader` project) when one is chosen.
