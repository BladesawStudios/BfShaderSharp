# BfShaderSharp

Fast reader for the Nintendo BFSHA shader archive and BNSH shader binary formats on Switch and Switch 2.

```csharp
BfshaFile archive = BfshaFile.Read(File.ReadAllBytes("Model.bfsha"));   // decompressed bytes
ShaderModel model = archive.Model("model")!.Value;
int program = model.CreateLookup().Find(new Dictionary<string, string> { ["enable_shadows"] = "1" });
ShaderStageCode fragment = model.Bnsh.Variation(model.Program(program).VariationIndex).Program.Code(ShaderStage.Fragment)!.Value;
string glsl = ShaderDecompiler.ToGlsl(fragment);   // BfShaderSharp.Decompile
```

Projects: `BfShaderSharp` (readers), `BfShaderSharp.Decompile` (stage code to GLSL), `BfShaderSharp.Preparation` (export programs to
GLSL and JSON for any game), `BfShaderSharp.Cli` (`bfshader list|export`). The decompiler needs the native lowering DLL from
`tools/AmpereLowerOracle` (set `BFS_AMPERE_LOWER`) and runs on Windows.

## Build

```bash
git submodule update --init
dotnet test BfShaderSharp.sln
```

Set `BFSHA_CORPUS` to one or more game `Shader` directories (separated by `;`) to run the corpus tests. Code conventions are in
[docs/guidelines.md](docs/guidelines.md).
