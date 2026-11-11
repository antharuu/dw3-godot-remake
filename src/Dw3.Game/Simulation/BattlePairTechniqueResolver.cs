namespace Dw3Production.Simulation;

/// <summary>Executes a source-qualified pair technique after its two partners have been selected.</summary>
public static class BattlePairTechniqueResolver
{
    /// <summary>Validates and resolves one pair technique without allowing a partial MP debit.</summary>
    /// <param name="state">Battle state receiving the resolved action.</param>
    /// <param name="incomingSlot">Selected reserve fighter slot.</param>
    /// <param name="definition">Pair relation decoded for the active form.</param>
    /// <param name="incomingNameId">Decoded name id of the selected incoming form.</param>
    /// <param name="tech">Decoded pair technique.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether the pair technique was accepted and resolved.</returns>
    public static bool TryExecute(BattleState state, int incomingSlot, PairTechniqueDefinition definition, int incomingNameId, TechData tech, IBattleRng rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(tech);
        ArgumentNullException.ThrowIfNull(rng);

        BattleFighter? active = state.ActivePlayerFighter;
        BattleFighter? incoming = state.FighterAt(BattleSide.Player, incomingSlot);
        BattleFighter? target = state.ActiveEnemyFighter;
        if (active is null || incoming is null || target is null || !BattlePairTechniqueRules.CanExecute(active, incoming, target, incomingSlot, incomingNameId, definition, tech))
        {
            return false;
        }

        active.Mp -= tech.Mp;
        incoming.Mp -= tech.Mp;
        state.ResolvePairTechniqueHits(active, target, tech, rng);
        state.MarkPairIncomingFought(incomingSlot);
        if (BattleRoster.AnyLiving(state.Enemies))
        {
            state.ActivatePairIncoming(incomingSlot);
        }

        return true;
    }
}
