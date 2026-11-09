namespace BfShaderSharp.Preparation;

/// <summary>A stage that could not be decompiled: where it is and why.</summary>
/// <remarks>The stage is stored as absent in the prepared file, so the rest of its program is still usable.</remarks>
public sealed record StageFailure(string Model, int Variation, string Stage, string Reason);

/// <summary>What a preparation run produced: counts and the stages that failed.</summary>
/// <remarks>Stages counts every compiled stage seen; Decompiled counts the distinct ones that were translated, since identical stage code is translated once.</remarks>
public sealed record PreparationResult(int Models, int Programs, int Stages, int Decompiled, IReadOnlyList<StageFailure> Failures);
