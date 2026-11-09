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
one prepared file for any game), `BfShaderSharp.Materials` (pick the program a BFRES material runs, over BfresSharp), `BfShaderSharp.Assets` (prepare a romfs's models, materials and textures into one asset pack), `BfShaderSharp.Cli` (`bfshader list|prepare|show|select|assets|inspect`). The decompiler lowers Ampere to Maxwell in managed code, so it runs on any platform .NET does; the prebuilt
Ryujinx translator DLLs in `lib/ryujinx` are the remaining platform-neutral dependency. The native lowering in `tools/AmpereLowerOracle` is only the reference the tests compare against (`BFS_AMPERE_LOWER`).

## Prepared files

`bfshader prepare Model.bfsha.zs out.bfsp` decompiles every program once (identical stages are translated once) and writes a
single `BFSP` file: fixed-size tables, a deduplicated string pool, and Brotli-compressed GLSL and default-buffer blobs stored in
content-hash order, so the output is byte-identical whatever the thread count. `PreparedArchive.Read` reads it in place, and
`PreparedModel.CreateLookup` finds a program from option choices without loading any GLSL. `bfshader show` inspects one. Open it with `PreparedArchive.Open(path)`, which memory-maps the file so only the programs you read are loaded. Preparing is allocation-heavy: the CLI runs with server GC (7x faster on the OOT Model archive, 29 s), and a host that calls `ArchivePreparer` should set `<ServerGarbageCollection>true</ServerGarbageCollection>` too.

## Choosing a program for a material

`MaterialShaderSelector.For(archive, names).Select(material)` returns the program of a shading model that a BFRES material runs. The material's options pick the static choices (a Switch 2 file keys them by name hash; `names` is the hash map of the `ExternalBinaryString` file in the game's Shader folder), its render infos pick the render state, alpha test, display face and pass options, and options the material leaves alone take the shader's defaults. The program is the one with exactly those choices. When the game never compiled that row, the program that keeps the most important choices is returned instead: draw-time choices first, then the material's own, then its `o_` options, which a renderer overrides anyway. The renderer's own draw-time options (skin weight count, probe and wetness options) are not the material's: pass them as `Select(material, drawTime)` and they win over the material. `bfshader select <file.bfsp> <model.bfres[.zs]> [<material>] [--option <name>=<choice>]...` prints the program of each material, reading the name table from the nearest ancestor romfs.

## Asset packs

`bfshader assets <romfs> <out.bfap> [--model <pattern>]... [--all-textures|--no-textures] [--shaders <folder>]` prepares the models, materials and textures of a romfs into one `BFAP` file. `--model` selects model files by the part of their name before the first dot (`Land_Deku*`); with none given every model file is prepared, and the same code path prepares one file or all of them.

A pack is content-addressed. Every piece (vertex stream, index buffer, shape, skeleton, material, texture file) is a blob named by the 128-bit hash of its bytes, and records refer to each other by that key, so a piece that models share is stored once: identical materials, skeletons, geometry and textures across the OOT romfs collapse to one blob each. A material record holds the shader program number its options select, so a renderer never touches the shader tables; the pack records the hash of each shader archive the numbers refer to. Textures are stored as the zstd frame the game shipped, so preparing them costs no compression and the stored size is the game's own; `AssetPack.Blob` returns the frame and the id of the dictionary it needs, for the caller to decompress (`ZsDic.Decompress` takes it as it is). Geometry is Brotli-compressed, and the reader reads blobs from the file on request, so a pack of many gigabytes is not mapped or loaded.

`--shaders <folder>` also prepares the romfs's shader archives into `.bfsp` files, but only for the programs the models use, and starts as soon as the models are imported so it runs while the textures are read. On the OOT romfs (7,896 model files, 21,515 materials, 17,765 textures, 24 cores) the whole run takes about 25 s: a 9.0 GB pack and 92 MB of prepared Model shaders for the 25,162 programs' used subset. `--all-programs` prepares every program instead. `bfshader inspect <file.bfap> [<model>]` lists a pack or a model.

Models are named by their file's name before the first dot, or `name/model` for a file that holds several. A sampled texture that has no file in the Tex folder (the player's are supplied elsewhere) is reported, not stored.

## Build

```bash
git submodule update --init --recursive
dotnet test BfShaderSharp.sln
```

Set `BFSHA_CORPUS` to one or more game `Shader` directories (separated by `;`) to run the corpus tests. Code conventions are in
[docs/guidelines.md](docs/guidelines.md).

## Licensing notes

`BfShaderSharp.Decompile/Ampere` is a C# port of the Ampere-to-Maxwell lowering of the Eden emulator (GPL-3.0-or-later) as carried in the Fritchii fork, using the Mesa NAK instruction encoding tables (MIT) as its reference. Both notices apply to that code.
