namespace Dw3Production.Simulation;

/// <summary>Describes one partner's form-experience settlement.</summary>
/// <param name="Slots">One row per used equipped slot, in slot order.</param>
/// <param name="LearnedForms">Form ids unlocked in evolution-list order.</param>
/// <param name="AcquiredFormCount">Acquired forms besides the base after the unlocks.</param>
public sealed record FormProgressionResult(IReadOnlyList<FightReportFormSlot> Slots, IReadOnlyList<int> LearnedForms, int AcquiredFormCount);
