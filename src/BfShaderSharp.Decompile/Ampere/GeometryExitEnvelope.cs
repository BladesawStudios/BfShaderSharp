namespace BfShaderSharp.Decompile.Ampere;

/// <summary>What the lowering needs to know about the exit handler of a native geometry program.</summary>
/// <remarks>
/// A geometry program installs a handler that finalizes its output and branches to it from every exit.
/// <see cref="HandlerOffset"/> is the handler's byte offset, <see cref="Exits"/> lists the EXIT instructions of the
/// body with their predicates, and <see cref="HasReturns"/> tells whether the body also returns from calls. A failed
/// analysis carries the byte offset of the instruction that broke the expected shape and a reason.
/// </remarks>
public sealed class GeometryExitEnvelope
{
    public List<(uint Offset, uint Predicate)> Exits { get; } = [];

    public uint HandlerOffset { get; set; }

    public bool HasReturns { get; set; }

    public uint ErrorOffset { get; private set; }

    public string? Error { get; private set; }

    public bool Succeeded => Error is null;

    public void Fail(int instructionIndex, string reason)
    {
        Exits.Clear();
        HandlerOffset = 0;
        HasReturns = false;
        ErrorOffset = (uint)(instructionIndex * 16);
        Error = reason;
    }
}
