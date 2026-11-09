# BfShaderSharp

Fast reader for the Nintendo BFSHA shader archive and BNSH shader binary formats on Switch and Switch 2.

```csharp
BfshaFile archive = BfshaFile.Read(File.ReadAllBytes("Model.bfsha"));   // decompressed bytes
ShaderModel model = archive.Model("model")!.Value;
int program = model.CreateLookup().Find(new Dictionary<string, string> { ["enable_shadows"] = "1" });
ShaderStageCode fragment = model.Bnsh.Variation(model.Program(program).VariationIndex).Program.Code(ShaderStage.Fragment)!.Value;
string glsl = ShaderDecompiler.ToGlsl(fragment);   // BfShaderSharp.Decompile
```

Projects: `BfShaderSharp` (readers), `BfShaderSharp.Decompile` (stage code to GLSL), `BfShaderSharp.Preparation` (turn an archive into
one prepared file for any game), `BfShaderSharp.Cli` (`bfshader list|prepare|show`). The decompiler needs the native lowering DLL from
`tools/AmpereLowerOracle` (set `BFS_AMPERE_LOWER`) and runs on Windows.

## Prepared files

`bfshader prepare Model.bfsha.zs out.bfsp` decompiles every program once (identical stages are translated once) and writes a
single `BFSP` file: fixed-size tables, a deduplicated string pool, and Brotli-compressed GLSL and default-buffer blobs stored in
content-hash order, so the output is byte-identical whatever the thread count. `PreparedArchive.Read` reads it in place, and
`PreparedModel.CreateLookup` finds a program from option choices without loading any GLSL. `bfshader show` inspects one. Open it with `PreparedArchive.Open(path)`, which memory-maps the file so only the programs you read are loaded. Preparing is allocation-heavy: the CLI runs with server GC (7x faster on the OOT Model archive, 29 s), and a host that calls `ArchivePreparer` should set `<ServerGarbageCollection>true</ServerGarbageCollection>` too.

## Build

```bash
git submodule update --init
dotnet test BfShaderSharp.sln
```

Set `BFSHA_CORPUS` to one or more game `Shader` directories (separated by `;`) to run the corpus tests. Code conventions are in
[docs/guidelines.md](docs/guidelines.md).
