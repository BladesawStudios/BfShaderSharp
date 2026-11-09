# TODO

## Readers

- BFSHA: options, key table and program lookup, attributes, samplers, uniform blocks and their uniforms, per-program binding tables, symbols. Models read today: names, counts, BNSH.
- BFSHA versions other than 9 (the old library read 4 to 9); each version is a layout class chosen once.
- `ResDict.IndexOf` is a linear scan; walk the patricia tree if lookups show up in profiles.
- Corpus survey: add a time budget and the BotW/TOTK corpora.

## Switch 2

- The OOT bench shaders (`Nin_Ounce_NVN2`, BFSHA 9, BNSH 2.4.1, GRSC api 4) are not Maxwell. A stage's byte code is a 0x80-byte header followed by 128-bit instructions (`0x7802` MOV, `0x794d` EXIT, `0x7918` NOP), which is the Turing/Ampere SASS encoding, not the 64-bit Maxwell encoding with scheduling words that Ryujinx translates (its code slices a 0x30 header). Ryujinx cannot decompile these. The decompiler project needs an Ampere SASS decoder (Mesa's NAK is the public reference for the SM70+ encoding).
- Control code is still present (`0x...` words such as `34127698 c9000000`); work out whether it locates immediate constants as it does on Switch 1.

## Later

- `BfShaderSharp.Decompile`: SASS to GLSL, separate from the readers.
- `BfShaderSharp.Preparation`: game-agnostic extraction of programs, reflection and constants to files.
- SHARCFB (AGL) archives.
- Writers, if a use turns up.
