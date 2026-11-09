# BfShaderSharp

Fast reader for the Nintendo BFSHA shader archive and BNSH shader binary formats on Switch and Switch 2.

```csharp
// Planned API; see TODO.md for what exists today.
BfshaFile archive = BfshaFile.Read(File.ReadAllBytes("Model.bfsha"));
```

## Build

```bash
git submodule update --init
dotnet test BfShaderSharp.sln
```

Set `BFSHA_CORPUS` to one or more game `Shader` directories (separated by `;`) to run the corpus tests. Code conventions are in
[docs/guidelines.md](docs/guidelines.md).
