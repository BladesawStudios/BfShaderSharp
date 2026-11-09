# TODO

## Readers

- BFSHA: options, key table and program lookup, attributes, samplers, uniform blocks and their uniforms, per-program binding tables, symbols. Models read today: names, counts, BNSH.
- BFSHA versions other than 9 (the old library read 4 to 9); each version is a layout class chosen once.
- `ResDict.IndexOf` is a linear scan; walk the patricia tree if lookups show up in profiles.
- Corpus survey: add a time budget and the BotW/TOTK corpora.

## Switch 2

- The OOT shaders are `Nin_Ounce_NVN2` builds. Check whether the program binaries are still Maxwell SASS (Ryujinx-translatable) or Ampere; this decides the decompiler project.

## Later

- `BfShaderSharp.Decompile`: SASS to GLSL, separate from the readers.
- `BfShaderSharp.Preparation`: game-agnostic extraction of programs, reflection and constants to files.
- SHARCFB (AGL) archives.
- Writers, if a use turns up.
