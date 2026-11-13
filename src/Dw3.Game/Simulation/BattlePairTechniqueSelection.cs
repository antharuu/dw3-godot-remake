using System.Diagnostics.CodeAnalysis;

namespace Dw3Production.Simulation;

/// <summary>Resolves a pair-menu selection against the active form's decoded relation without mutating battle state.</summary>
public static class BattlePairTechniqueSelection
{
    /// <summary>Resolves the exact relation, technique and incoming source name for a selectable pair action.</summary>
    /// <param name="flow">Active flow containing the form, name and technique catalogues.</param>
    /// <param name="incomingSlot">Party slot selected by the switch menu.</param>
    /// <param name="definition">Resolved relation from decoded active-form data.</param>
    /// <param name="technique">Resolved technique matching the relation's exact id.</param>
    /// <param name="incomingNameId">Decoded DIGNAM id of the selected incoming form.</param>
    /// <returns>Whether all inputs are present and legal before resource mutation or RNG use.</returns>
    public static bool TryResolve(
        BattleFlowController flow,
        int incomingSlot,
        [NotNullWhen(true)] out PairTechniqueDefinition? definition,
        [NotNullWhen(true)] out TechData? technique,
        out int incomingNameId)
    {
        ArgumentNullException.ThrowIfNull(flow);
        definition = null;
        technique = null;
        incomingNameId = 0;
        BattleFighter? active = flow.State.ActivePlayerFighter;
        BattleFighter? incoming = flow.State.FighterAt(BattleSide.Player, incomingSlot);
        if (active is null
            || incoming is null
            || !flow.PairTechniquesByForm.TryGetValue(active.Id, out PairTechniqueDefinition? candidateDefinition)
            || candidateDefinition is null
            || !flow.Techs.TryGetValue(candidateDefinition.TechniqueId, out TechData? candidateTechnique)
            || candidateTechnique is null
            || !flow.NameIds.TryGetValue(incoming.Id, out incomingNameId))
        {
            return false;
        }

        if (!BattlePairTechniqueRules.CanExecute(active, incoming, flow.State.ActiveEnemyFighter, incomingSlot, incomingNameId, candidateDefinition, candidateTechnique))
        {
            return false;
        }

        definition = candidateDefinition;
        technique = candidateTechnique;
        return true;
    }
}
