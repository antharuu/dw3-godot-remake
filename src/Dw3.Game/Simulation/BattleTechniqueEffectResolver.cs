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
    /// <param name="landedDamage">Formula damage before HP clamping from the action's landed sequence, used by the drain formula.</param>
    /// <param name="landedTarget">Original target of a valid landed hit, retained for effect-6 after lethal ordinary damage.</param>
    /// <returns>Whether this bounded resolver supports the effect, regardless of its resistance roll.</returns>
    public static bool ApplyLanded(BattleState state, BattleSide attackerSide, TechData technique, IBattleRng rng, int landedDamage, BattleFighter? landedTarget = null)
    {
        switch (technique.Effect)
        {
            case BattleTechniqueEffect.NoSpecialEffect:
                return true;
            case BattleTechniqueEffect.DoubleMagic:
                return true;
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
            case BattleTechniqueEffect.KnockOut:
                ApplyKnockOut(state, attackerSide, technique.EffectChance, rng, landedTarget);
                return true;
            case BattleTechniqueEffect.Drain:
                _ = BattleResourceEffects.DrainHp(state, attackerSide, technique.EffectChance, technique.EffectPower, landedDamage, rng);
                return true;
            case BattleTechniqueEffect.Steal:
                _ = BattleResourceEffects.Steal(state, attackerSide, technique.EffectChance, rng);
                return true;
            case BattleTechniqueEffect.DrainMp:
                _ = BattleResourceEffects.DrainMp(state, attackerSide, technique.EffectPower);
                return true;
            case BattleTechniqueEffect.NoSwitch:
            case BattleTechniqueEffect.NoDigivolve:
                _ = attackerSide == BattleSide.Enemy && state.ApplyRestriction(technique.Effect, technique.EffectChance, technique.EffectPower, rng);
                return true;
            case BattleTechniqueEffect.LowerAttack:
            case BattleTechniqueEffect.LowerDefense:
                _ = state.ApplyStatLowering(attackerSide, technique.Effect, technique.EffectPower, rng);
                return true;
            case BattleTechniqueEffect.RaiseOneStatus:
            case BattleTechniqueEffect.RaiseEachStatus:
            case BattleTechniqueEffect.RaiseAllStatus:
                _ = state.ApplyStatusRaise(technique.Effect, technique.EffectChance, technique.EffectPower, rng);
                return true;
            case BattleTechniqueEffect.EnemyOnly:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Returns whether this side owns the target contract of the selected action.</summary>
    /// <param name="attackerSide">Side attempting the technique.</param>
    /// <param name="technique">Technique to check.</param>
    /// <returns><see langword="true"/> when this side may execute the technique's effect.</returns>
    public static bool CanExecute(BattleSide attackerSide, TechData technique) => technique.Effect switch
    {
        BattleTechniqueEffect.NoSpecialEffect => technique.Icon is BattleConst.TechPhysical or BattleConst.TechMagic,
        BattleTechniqueEffect.DoubleMagic => technique.Icon == BattleConst.TechMagic,
        BattleTechniqueEffect.DrainMp => attackerSide == BattleSide.Enemy,
        BattleTechniqueEffect.Steal => attackerSide == BattleSide.Player,
        _ => technique.Icon <= BattleConst.TechMagic,
    };

    /// <summary>Returns whether the technique uses the generic hit path before its effect.</summary>
    /// <param name="technique">Technique to test.</param>
    /// <returns><see langword="true"/> when a hit sequence must land before the effect can fire.</returns>
    public static bool RequiresHitSequence(TechData technique) => technique.Effect switch
    {
        BattleTechniqueEffect.NoSpecialEffect => true,
        BattleTechniqueEffect.DoubleMagic => true,
        BattleTechniqueEffect.DrainMp or BattleTechniqueEffect.EnemyOnly => false,
        _ => true,
    };

    /// <summary>Applies effect 6 through its normal direct contract or the narrower post-hit contract.</summary>
    /// <param name="state">Battle receiving the effect.</param>
    /// <param name="attackerSide">Side that landed the technique.</param>
    /// <param name="chance">Extracted source chance.</param>
    /// <param name="rng">Shared battle random stream.</param>
    /// <param name="landedTarget">Original target captured by the valid action, if any.</param>
    private static void ApplyKnockOut(BattleState state, BattleSide attackerSide, int chance, IBattleRng rng, BattleFighter? landedTarget)
    {
        _ = landedTarget is null
            ? state.TryImmediateKnockOut(attackerSide, chance, rng)
            : BattleKnockOutResolver.TryApplyAfterLandedHit(state, attackerSide, landedTarget, chance, rng);
    }
}
