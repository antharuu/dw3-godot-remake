namespace Dw3Production.Simulation;

/// <summary>Describes one party member's reward state after a battle.</summary>
public sealed record FightReportPartner(
    int PartyIndex,
    int PartnerId,
    int FighterId,
    int LevelBefore,
    int ExpBefore,
    int ExpGained,
    int LevelAfter,
    IReadOnlyList<int> SlotDigimonIds)
{
    /// <summary>Gets the settled form slots, or an empty list when no form progression was applied.</summary>
    public IReadOnlyList<FightReportFormSlot> FormSlots { get; init; } = [];

    /// <summary>Gets the newly unlocked forms, or an empty list when none were acquired.</summary>
    public IReadOnlyList<FightReportLearnedForm> LearnedForms { get; init; } = [];
}
