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
    IReadOnlyList<int> SlotDigimonIds);
