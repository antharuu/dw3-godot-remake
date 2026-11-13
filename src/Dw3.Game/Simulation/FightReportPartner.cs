namespace Dw3Production.Simulation;

/// <summary>Describes one party member's reward state after a battle.</summary>
/// <param name="PartyIndex">Party slot index.</param>
/// <param name="PartnerId">Partner identity used by the level message.</param>
/// <param name="FighterId">Fighter id shown on the panel.</param>
/// <param name="LevelBefore">Partner level before this battle's experience.</param>
/// <param name="ExpBefore">Partner experience before this battle.</param>
/// <param name="ExpGained">Partner experience gained.</param>
/// <param name="LevelAfter">Partner level after this battle's experience.</param>
/// <param name="SlotDigimonIds">The up to three equipped form slots during the battle, never the base form row.</param>
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
    /// <summary>Gets one settled row per used equipped slot, or an empty list when no slot was used.</summary>
    public IReadOnlyList<FightReportFormSlot> FormSlots { get; init; } = [];

    /// <summary>Gets the levels of the three equipped forms as they were displayed after settlement, in source slot order.</summary>
    public IReadOnlyList<int> SlotLevels { get; init; } = [];

    /// <summary>Gets the newly unlocked forms, or an empty list when none were acquired.</summary>
    public IReadOnlyList<FightReportLearnedForm> LearnedForms { get; init; } = [];

    /// <summary>Gets the acquired forms besides the base after the unlocks. Four or more after an unlock shows the slot notice.</summary>
    public int AcquiredFormCount { get; init; }
}
