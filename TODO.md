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
- Compressed cubic curves decode with `CurveDecoder` to a value and a slope per key (a cubic Hermite spline), and the 0.2% flagged as plain keys are rotation curves of a unit quaternion a key (`DecodeRotation`, shortest-arc interpolation; the component order x, y, z, w is assumed from the bone's rotation, not checked). Linear, step and baked compressed curves do not occur in the OOT animation files and are not decoded. The decoded values agree with the differences between keys (median correlation 0.98), but nothing has played them against the game yet.
- Material animations are stored by material and parameter name, with each uniform's block offset in the material's `UniformLayoutRecord`; a runtime that plays one joins the two by name.
- `ShaderSource` entries record which archive a program number came from; a reader does not yet check them against a `.bfsp`.
- SHARC files (`SharcbFile`, the AGL programs of `ApplicationPackage` and the foliage programs) are read and prepared with `SharcPreparer` (CLI: `prepare <package.sarc.zs> <out.bfsp> --entry AglShader.sharcb`): each program is a model and each option combination a program. OOT's `AglShader` (5,893 stages) and `FoliageShader` (353) prepare in about a second each with no failed stage. A compute stage has no program header (its code starts at byte 0 and its program counters count from there), and its group size is in the control block at 0x58; task and mesh stages have the 0x80-byte header, their thread count is the low byte of the control word at 0x58, and a mesh stage's vertex and primitive limits are the high and low half of the word at 0x60. Task and mesh stages take slots 6 and 7 of a variation (prepared file version 2) and decompile to GLSL for `GL_EXT_mesh_shader`: the stage buffer lives in workgroup memory and an entry point copies the task payload in and the vertex lines and primitive map out (a port of Fritchii's SPIR-V backend). Not settled for task and mesh: the topology is assumed to be triangles (the control word at 0x5c is not decoded), the payload size is fixed at 512 words (the hardware register that sets it is not in the file), per-primitive outputs are dropped because only the vertex output map is in the stage's own header, and depth is not converted to Vulkan's range. Compute, task and mesh bindings sit in the vertex slot. Verified on OOT data: a stage's code ends at its control block's program size (0x38), exactly at the padding of self-branches; the first option is the most significant digit of a combination (a vertex stage's code changes with only the two options it can use, REVERSE_X and USE_GAME_OVER_FILTER, in `hdr_compose`); task and mesh are named from their headers and absence of attributes, not from a draw.

## Later

- `BfShaderSharp.Decompile`: SASS to GLSL, separate from the readers.
- Writers, if a use turns up.
