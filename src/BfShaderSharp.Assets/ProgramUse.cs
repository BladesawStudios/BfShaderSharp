using System.Collections.Concurrent;

namespace BfShaderSharp.Assets;

/// <summary>The shader programs that imported materials run, by shader archive and shading model.</summary>
/// <remarks>
/// Filled while materials are imported, from many threads. <see cref="Of"/> gives the programs of one archive in the shape
/// the shader preparer takes, so an archive can be prepared for only the programs some models use.
/// </remarks>
public sealed class ProgramUse
{
    private readonly ConcurrentDictionary<(string Archive, string Model), ConcurrentDictionary<int, byte>> _programs = new();

    public int Count => _programs.Values.Sum(p => p.Count);

    public void Add(string archive, string model, int program)
    {
        if (program >= 0) _programs.GetOrAdd((archive, model), _ => new ConcurrentDictionary<int, byte>()).TryAdd(program, 0);
    }

    public IReadOnlyDictionary<string, IReadOnlySet<int>> Of(string archive) =>
        _programs.Where(p => p.Key.Archive == archive).ToDictionary(p => p.Key.Model, p => (IReadOnlySet<int>)p.Value.Keys.ToHashSet());
}
