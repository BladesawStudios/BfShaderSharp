# TODO

## Readers

- BFSHA: attributes, uniform blocks and their uniforms (names, offsets, default buffers), sampler/image names, storage buffers and symbols. Done: option tables, key table, `ProgramLookup`, program records with binding tables.
- BFSHA versions other than 9 (the old library read 4 to 9); each version is a layout class chosen once.
- `ResDict.IndexOf` is a linear scan; walk the patricia tree if lookups show up in profiles.
- Corpus survey: add a time budget and the BotW/TOTK corpora.

## Switch 2 decompiler

- `BfShaderSharp.Decompile` lowers Ampere to Maxwell in managed code (`Ampere/`, a port of the Fritchii lowering) and translates with the prebuilt Ryujinx DLLs in `lib/ryujinx`. The managed lowering matches the native one word for word on all 11,734 distinct OOT stage programs, rejecting the same ones for the same reasons (`AmpereParityTests`, with `BFS_AMPERE_LOWER` and `BFSHA_CORPUS`); 48,580 of 48,585 stages lower. `tools/AmpereLowerOracle` builds the native reference for that test only.
- Replace the prebuilt Ryujinx binaries with a source build.
- Control code and immediate constants are not used. Bindless texture handles come out as `fp_t_tcb_*` names; resolving them needs the reflection and the texture-buffer constant slot.
- 5 fragment programs use an opcode the lowering rejects (`unsupported native opcode`).

## Preparation

- Switch 2 stages carry no per-stage reflection (the BNSH reflection offset is zero), so prepared files carry no stage slot lists; bindings come from the BFSHA program tables. Bindless textures leave the sampler lists empty too.
- A GLSL post-pass that names the `fp_t_tcb_*` bindless handles, and sampler names from the BFSHA symbol table.
- Resume an interrupted prepare; stream blobs to disk instead of holding them all in memory.

## Asset packs

- An optional tier that stores textures deswizzled and recompressed, for the fastest GL upload, at the cost of prepare time and size.
- Switch 2 model packages (`.bfres.mc`, MCPK) are skipped for good: the game does not use them, so those five models are unused.
- Compressed cubic curves are stored as the file holds them but not decoded. All 4.6 million skeletal curves of the OOT animation files are cubic and compressed, with an 8 byte header of two floats and two signed bytes or int16s a key; a decode of the key pairs as a value and a slope scaled by the header floats does not match the finite differences of the values (median relative error 0.78), so the layout is something else. Until it is worked out, a renderer cannot evaluate them; bone, material, visibility and shape animations are all in the pack.
- Material animations are stored by material and parameter name, with each uniform's block offset in the material's `UniformLayoutRecord`; a runtime that plays one joins the two by name.
- `ShaderSource` entries record which archive a program number came from; a reader does not yet check them against a `.bfsp`.

## Later

- `BfShaderSharp.Decompile`: SASS to GLSL, separate from the readers.
- SHARCFB (AGL) archives.
- Writers, if a use turns up.
