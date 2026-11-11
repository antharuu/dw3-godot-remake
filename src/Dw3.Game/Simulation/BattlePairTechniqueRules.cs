namespace Dw3Production.Simulation;

/// <summary>Evaluates source-qualified pair-technique eligibility before a pair action mutates battle state.</summary>
public static class BattlePairTechniqueRules
{
    /// <summary>Checks the required matching name, living status, restrictions and shared MP cost for a pair technique.</summary>
    /// <param name="active">Current outgoing partner.</param>
    /// <param name="incoming">Selected incoming partner.</param>
    /// <param name="target">Current enemy target.</param>
    /// <param name="incomingSlot">Slot of the incoming partner.</param>
    /// <param name="incomingNameId">Name-table id of the incoming form.</param>
    /// <param name="definition">Pair relation defined by the active form.</param>
    /// <param name="tech">Technique whose MP is charged to both partners.</param>
    /// <returns>Whether the action is allowed before any resource is spent.</returns>
    public static bool CanExecute(BattleFighter? active, BattleFighter? incoming, BattleFighter? target, int incomingSlot, int incomingNameId, PairTechniqueDefinition definition, TechData tech)
    {
        return active is { IsKnockedOut: false, Temporary: false }
            && incoming is { IsKnockedOut: false }
            && target is not null
            && incomingSlot >= 0
            && !ReferenceEquals(active, incoming)
            && incomingNameId == definition.PartnerNameId
            && (active.Flags & (FighterFlags.NoDigivolve | FighterFlags.Asleep)) == 0
            && (incoming.Flags & FighterFlags.NoDigivolve) == 0
            && active.Mp >= tech.Mp
            && incoming.Mp >= tech.Mp
            && BattleTechniqueEffectResolver.CanExecute(BattleSide.Player, tech);
    }
}
