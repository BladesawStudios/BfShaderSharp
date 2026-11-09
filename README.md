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
one prepared file for any game), `BfShaderSharp.Materials` (pick the program a BFRES material runs, over BfresSharp), `BfShaderSharp.Cli` (`bfshader list|prepare|show|select`). The decompiler lowers Ampere to Maxwell in managed code, so it runs on any platform .NET does; the prebuilt
Ryujinx translator DLLs in `lib/ryujinx` are the remaining platform-neutral dependency. The native lowering in `tools/AmpereLowerOracle` is only the reference the tests compare against (`BFS_AMPERE_LOWER`).

## Prepared files

`bfshader prepare Model.bfsha.zs out.bfsp` decompiles every program once (identical stages are translated once) and writes a
single `BFSP` file: fixed-size tables, a deduplicated string pool, and Brotli-compressed GLSL and default-buffer blobs stored in
content-hash order, so the output is byte-identical whatever the thread count. `PreparedArchive.Read` reads it in place, and
`PreparedModel.CreateLookup` finds a program from option choices without loading any GLSL. `bfshader show` inspects one. Open it with `PreparedArchive.Open(path)`, which memory-maps the file so only the programs you read are loaded. Preparing is allocation-heavy: the CLI runs with server GC (7x faster on the OOT Model archive, 29 s), and a host that calls `ArchivePreparer` should set `<ServerGarbageCollection>true</ServerGarbageCollection>` too.

## Choosing a program for a material

`MaterialShaderSelector.For(archive, names).Select(material)` returns the program of a shading model that a BFRES material runs. The material's options pick the static choices (a Switch 2 file keys them by name hash; `names` is the hash map of the `ExternalBinaryString` file in the game's Shader folder), its render infos pick the render state, alpha test, display face and pass options, and options the material leaves alone take the shader's defaults. The program is the one with exactly those choices. When the game never compiled that row, the program that keeps the most important choices is returned instead: draw-time choices first, then the material's own, then its `o_` options, which a renderer overrides anyway. The renderer's own draw-time options (skin weight count, probe and wetness options) are not the material's: pass them as `Select(material, drawTime)` and they win over the material. `bfshader select <file.bfsp> <model.bfres[.zs]> [<material>] [--option <name>=<choice>]...` prints the program of each material, reading the name table from the nearest ancestor romfs.

## Build

```bash
git submodule update --init
dotnet test BfShaderSharp.sln
```

Set `BFSHA_CORPUS` to one or more game `Shader` directories (separated by `;`) to run the corpus tests. Code conventions are in
[docs/guidelines.md](docs/guidelines.md).

## Licensing notes

`BfShaderSharp.Decompile/Ampere` is a C# port of the Ampere-to-Maxwell lowering of the Eden emulator (GPL-3.0-or-later) as carried in the Fritchii fork, using the Mesa NAK instruction encoding tables (MIT) as its reference. Both notices apply to that code.
