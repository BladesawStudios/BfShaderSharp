# TODO

## Readers

- BNSH container: header, relocation table, shader variations, reflection. Bench: Switch 2 `OOTOfTime` Shader folder.
- BFSHA archive: shading models, options, program lookup, sampler and uniform block bindings.
- Corpus survey tests with a time budget.

## Switch 2

- The OOT shaders are `Nin_Ounce_NVN2` builds. Check whether the program binaries are still Maxwell SASS (Ryujinx-translatable) or Ampere; this decides the decompiler project.

## Later

- `BfShaderSharp.Decompile`: SASS to GLSL, separate from the readers.
- `BfShaderSharp.Preparation`: game-agnostic extraction of programs, reflection and constants to files.
- SHARCFB (AGL) archives.
- Writers, if a use turns up.
