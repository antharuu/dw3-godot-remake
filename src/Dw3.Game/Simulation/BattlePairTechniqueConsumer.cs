namespace Dw3Production.Simulation;

/// <summary>Consumes a qualified pair selection through the authoritative battle state and phase machine.</summary>
public static class BattlePairTechniqueConsumer
{
    /// <summary>Executes a source-qualified pair technique, or refuses before mutation when the menu selection is no longer legal.</summary>
    /// <param name="flow">Active battle flow.</param>
    /// <param name="incomingSlot">Reserve party slot selected by the menu.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether the pair action spent the player's turn.</returns>
    public static bool TryChoose(BattleFlowController flow, int incomingSlot, IBattleRng rng)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(rng);
        if (flow.Phase != BattleFlowPhase.PlayerCommand
            || !BattlePairTechniqueSelection.TryResolve(flow, incomingSlot, out PairTechniqueDefinition? definition, out TechData? technique, out int incomingNameId)
            || !flow.State.PlayerPairTechnique(incomingSlot, definition, incomingNameId, technique, rng))
        {
            return false;
        }

        flow.AdvanceAfterPairTechnique();
        return true;
    }
}
