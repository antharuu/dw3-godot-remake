namespace Dw3Production.Simulation;

/// <summary>Describes new persistent techniques and forms produced by one form-experience settlement.</summary>
/// <param name="LearnedTechniques">Technique ids learned in source order.</param>
/// <param name="LearnedForms">Form ids unlocked in source order.</param>
public sealed record FormProgressionResult(IReadOnlyList<int> LearnedTechniques, IReadOnlyList<int> LearnedForms);
