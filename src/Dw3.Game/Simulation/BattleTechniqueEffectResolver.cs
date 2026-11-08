namespace Dw3Production.Simulation;

/// <summary>Applies the bounded set of status effects after a technique has landed.</summary>
public static class BattleTechniqueEffectResolver
{
    /// <summary>
    /// Applies a supported effect using the source effect chance and strength. Unsupported effect
    /// families intentionally return <see langword="false"/> without mutating the battle.
    /// </summary>
    /// <param name="state">Battle receiving the effect.</param>
    /// <param name="attackerSide">Side that landed the technique.</param>
    /// <param name="technique">Technique holding the extracted effect fields.</param>
    /// <param name="rng">Shared battle random stream.</param>
    /// <returns>Whether this bounded resolver supports the effect, regardless of its resistance roll.</returns>
    public static bool ApplyLanded(BattleState state, BattleSide attackerSide, TechData technique, IBattleRng rng)
    {
        switch (technique.Effect)
        {
            case BattleTechniqueEffect.Poison:
                _ = state.InflictPoison(attackerSide, technique.EffectChance, technique.EffectPower, rng);
                return true;
            case BattleTechniqueEffect.Paralysis:
                _ = state.InflictParalysis(attackerSide, technique.EffectChance, technique.EffectPower, rng);
                return true;
            case BattleTechniqueEffect.Confusion:
                _ = state.InflictConfusion(attackerSide, technique.EffectChance, technique.EffectPower, rng);
                return true;
            case BattleTechniqueEffect.Sleep:
                _ = state.InflictSleep(attackerSide, technique.EffectChance, technique.EffectPower, rng);
                return true;
            default:
                return false;
        }
    }
}
