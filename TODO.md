# TODO

## Readers

- BFSHA: attributes, uniform blocks and their uniforms (names, offsets, default buffers), sampler/image names, storage buffers and symbols. Done: option tables, key table, `ProgramLookup`, program records with binding tables.
- BFSHA versions other than 9 (the old library read 4 to 9); each version is a layout class chosen once.
- `ResDict.IndexOf` is a linear scan; walk the patricia tree if lookups show up in profiles.
- Corpus survey: add a time budget and the BotW/TOTK corpora.

## Switch 2 decompiler

- `BfShaderSharp.Decompile` lowers Ampere to Maxwell through a native DLL (`tools/AmpereLowerOracle`, built from the header-only Fritchii lowering; set `BFS_AMPERE_LOWER`), then translates with the prebuilt Ryujinx DLLs in `lib/ryujinx`. Corpus result: 48,580 of 48,585 OOT stages lower, and a 1-in-10 sample translates all but the lowering rejects. The full corpus takes about 8 minutes (`BFSHA_VARIATION_STRIDE=10` for a 1-minute sample).
- Port the lowering (`lower_to_maxwell.h`, `register_pressure.h`, `geometry_exit.h`, about 4.5k lines) to C#, checked word for word against the native DLL on the corpus, to drop the native dependency. The Fritchii code is GPL-3.0-or-later with Mesa NAK (MIT) encoding references; keep the notices.
- Replace the prebuilt Ryujinx binaries with a source build.
- Control code and immediate constants are not used. Bindless texture handles come out as `fp_t_tcb_*` names; resolving them needs the reflection and the texture-buffer constant slot.
- 5 fragment programs use an opcode the lowering rejects (`unsupported native opcode`).

## Later

- `BfShaderSharp.Decompile`: SASS to GLSL, separate from the readers.
- `BfShaderSharp.Preparation`: game-agnostic extraction of programs, reflection and constants to files.
- SHARCFB (AGL) archives.
- Writers, if a use turns up.
