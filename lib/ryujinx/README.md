# Ryujinx shader translator (source)

`src/` holds the Maxwell shader translator of Ryubing (the Ryujinx fork, MIT, see `Ryujinx License.txt`), copied from
`OneOffs/Ryubing` at commit a61a42681 ("fix updater (#197)"): `Ryujinx.Graphics.Shader` and `Spv.Generator` unchanged, and
`Ryujinx.Common` trimmed to the few files the translator uses (BitUtils, BitfieldExtensions, ArrayPtr, IArray,
StructArrayHelpers, ObjectPool) plus a small `EmbeddedResources` that only reads the translator's GLSL helper files.
`BfShaderSharp.Decompile` references the translator project directly; `Directory.Build.props` here keeps the vendored code
out of the repository's warnings-as-errors rules. Improvements to the translator go in this folder; note each in this file.

## Changes from upstream

None to the translator source yet. The CLI runs it without tiered compilation (about 20% faster on a full prepare).
