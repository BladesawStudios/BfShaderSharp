# Ryujinx shader translator (source)

`src/` holds the Maxwell shader translator of Ryubing (the Ryujinx fork, MIT, see `Ryujinx License.txt`), copied from
`OneOffs/Ryubing` at commit a61a42681 ("fix updater (#197)"): `Ryujinx.Graphics.Shader` and `Spv.Generator` unchanged, and
`Ryujinx.Common` trimmed to the few files the translator uses (BitUtils, BitfieldExtensions, ArrayPtr, IArray,
StructArrayHelpers, ObjectPool) plus a small `EmbeddedResources` that only reads the translator's GLSL helper files.
`BfShaderSharp.Decompile` references the translator project directly; `Directory.Build.props` here keeps the vendored code
out of the repository's warnings-as-errors rules. Improvements to the translator go in this folder; note each in this file.

## Changes from upstream

The CLI runs the translator without tiered compilation (about 20% faster on a full prepare). Output is byte-identical to upstream for every change below.

- `NodeHash`: IR and AST nodes (`Operand`, `Operation`, `PhiNode`, `BasicBlock`, `AstNode`) carry a per-thread counter as their hash code instead of the slow default object hash. Collections keyed on them enumerate in insertion order, so nothing depends on the value.
- `Operand.UseOps` is created on first use, since only local variables have uses.
- `Operation` copies its operand arrays with a span instead of `Array.Clone`.
- `Utf8Code`: the GLSL generator writes UTF-8 straight into a per-thread reused buffer (`ShaderProgram.CodeUtf8`) instead of a `StringBuilder`, so a program's text no longer exists as a string, a byte array and a compression buffer on the large object heap. `ShaderProgram.Code` still works and builds the string on demand.
