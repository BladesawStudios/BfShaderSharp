# Guidelines

## Size

- Classes are about 256 lines at most.
- Methods are about 64 lines at most.
- A file longer than that is split by what its parts are, not by where the line fell: each part is a class that can be named.

## Comments

- Every class has a `<summary>` and a `<remarks>`, and nothing else is an XML doc comment: no `<param>`, `<returns>` or member docs.
- The `<summary>` is 1-2 sentences that say what the class is.
- The `<remarks>` says in more detail what the class does: what it holds, what it guarantees, what a reader needs to know before using it.
- Remarks describe the class as it is. They never refer to how it used to work, what it replaced, or what is planned.
- Every other comment is a regular `//` comment of 1-2 sentences, and only where the code cannot say it.
- No giant comments, no comments on every member, no history.

## Code

- No re-defining shared code. Behavior that more than one class needs (reading a table, resolving a string, formatting a value) is one class that the others call.
- No ad-hoc copies. A second definition that works slightly differently "for convenience" is a bug in the design: extend the shared class instead.
- Always abstract the shared part.
- Nothing is temporary: no stopgaps, no "for now".
- `// TODO:` is for work that genuinely has to wait. It says what the code does today and what would remove the limitation. Keep them sparse.
- A stopgap that prevents a partial implementation is the one allowed workaround. It is marked `// TODO:` and says it exists to prevent a partial implementation.

## Format design

- The model types (`Model`, `Material`, `Shape` and the rest) know nothing about how a file stores them: no offsets, no version checks, no platform branches.
- A version of the format is one layout class that reads and writes its structures. Version and platform differences are chosen once, where the layout is picked, and never tested inline in a reader.
- Nothing is parsed into static or ambient state. What a read needs (the string table, the buffer region, a name resolver) is carried by a context object that the read creates and owns, so any number of files can be read at once.
- Large data (vertex buffers, index buffers, texture levels, external files) stays a slice of the source bytes until it is asked for.
- A structure that is read and written back unchanged is written byte for byte. Fields the library does not understand are kept, not dropped.
- A structure the library cannot read yet is reported with `BfShaderUnsupportedException` or kept as bytes, never guessed at. The corpus survey is the acceptance test: every file of the Switch and Switch 2 corpora reads.

## Projects

- Dependencies point down. A project never references one above it, and a project lists what it may reference when it is added.
- The library reads decompressed bytes and references no compression library. Only the tests reference `ZsDicSharp`, to open compressed corpus files.
- Shader decompilation (SASS to GLSL) is its own project above the readers, so the readers carry no decompiler dependency.
- `BfShaderSharp.Preparation` is game-agnostic: it turns shader archives into files and knows no game, manifest or renderer format.
- Vendored libraries live in `lib/vendor` as submodules, one copy each.
- A project's namespaces start with its name. Folders inside a project are namespaces.
- A new project is added in the same commit as the first thing that belongs in it. An empty project is not created.

## Errors

- Malformed input throws `BfShaderFormatException`, with the offset when there is one. Nothing else is thrown for a bad file, and no runtime index error escapes a read.
- An exception is either handled or allowed to propagate. The library never swallows one.
- An empty `catch` is allowed only for a best-effort operation, narrows to the exception types it expects, and says what is lost.
- The library does not write to the console or any log. A caller learns what happened from return values and exceptions.

## Platforms

- The library builds for every platform .NET does and calls nothing operating-system specific.
- A path inside a game's files uses `/`.

## Tests

- Every class has tests. Header and structure tests use real bytes where there are any to take.
- Corpus tests run when `BFSHA_CORPUS` points at Shader directories, and are skipped otherwise.
- Speed is a requirement: reads slice the source bytes, parse tables lazily, and a corpus survey of the 43 MB Switch 2 `Model` archive has a time budget the tests assert.

## Process

- One system per branch, and one commit per class or per concern.
- A refactor changes no behavior. If behavior has to change, it is its own commit and says so.
- The build and tests pass before a branch is merged.
- Commits are authored by the person doing the work, with no co-author trailers.
