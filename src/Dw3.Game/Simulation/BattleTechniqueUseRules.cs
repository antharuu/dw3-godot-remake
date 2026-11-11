namespace Dw3Production.Simulation;

/// <summary>Centralizes pre-mutation player technique eligibility checks.</summary>
public static class BattleTechniqueUseRules
{
    /// <summary>Checks whether a living, unrestricted player may use the supplied technique against the current target.</summary>
    /// <param name="attacker">Active player fighter.</param>
    /// <param name="target">Active enemy fighter.</param>
    /// <param name="tech">Technique to use.</param>
    /// <param name="techs">Catalogue used to resolve composite damage entries.</param>
    /// <returns>Whether the technique can spend the player's turn.</returns>
    public static bool CanUse(BattleFighter? attacker, BattleFighter? target, TechData tech, IReadOnlyDictionary<int, TechData> techs)
    {
        return attacker is { IsKnockedOut: false }
            && target is not null
            && BattleTechniqueEffectResolver.CanExecute(BattleSide.Player, tech)
            && DoubleMagicTechniqueSelector.HasRequiredDamageEntries(tech, techs)
            && tech.Mp <= attacker.Mp
            && (attacker.Flags & (FighterFlags.Asleep | FighterFlags.Confused)) == 0;
    }
}
