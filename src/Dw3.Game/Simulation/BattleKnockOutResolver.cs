namespace Dw3Production.Simulation;

/// <summary>Applies the source immediate-knock-out effect without coupling its roll to a damage formula.</summary>
public static class BattleKnockOutResolver
{
    /// <summary>Attempts the effect-6 roll against the opposing active fighter and commits its knock-out transition on success.</summary>
    /// <param name="state">Battle containing the source and target fighters.</param>
    /// <param name="attackerSide">Side that landed the effect-6 technique.</param>
    /// <param name="chance">Extracted base application chance.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns><see langword="true"/> when the target is immediately knocked out, otherwise <see langword="false"/> without mutation.</returns>
    public static bool TryApply(BattleState state, BattleSide attackerSide, int chance, IBattleRng rng)
    {
        BattleSide targetSide = BattlePoisonRules.Opposite(attackerSide);
        return TryApply(state, attackerSide, state.FighterFor(targetSide), chance, rng, requireLivingTarget: true);
    }

    /// <summary>Attempts the effect-6 roll for the original target of a valid landed hit, retaining that action's target after ordinary damage knocked it out.</summary>
    /// <param name="state">Battle containing the source and target fighters.</param>
    /// <param name="attackerSide">Side that landed the effect-6 technique.</param>
    /// <param name="target">Original target captured before the landed hit sequence.</param>
    /// <param name="chance">Extracted base application chance.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns><see langword="true"/> when the source roll succeeds for the original target, otherwise <see langword="false"/>.</returns>
    public static bool TryApplyAfterLandedHit(BattleState state, BattleSide attackerSide, BattleFighter target, int chance, IBattleRng rng) =>
        TryApply(state, attackerSide, target, chance, rng, requireLivingTarget: false);

    /// <summary>Rolls and commits the effect only when the caller's target contract permits it.</summary>
    /// <param name="state">Battle containing the source and target fighters.</param>
    /// <param name="attackerSide">Side that landed the effect-6 technique.</param>
    /// <param name="target">Target selected by the caller's contract.</param>
    /// <param name="chance">Extracted base application chance.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <param name="requireLivingTarget">Whether the direct public contract rejects an already knocked-out target.</param>
    /// <returns><see langword="true"/> when the source roll succeeds, otherwise <see langword="false"/>.</returns>
    private static bool TryApply(BattleState state, BattleSide attackerSide, BattleFighter? target, int chance, IBattleRng rng, bool requireLivingTarget)
    {
        if (attackerSide == BattleSide.Player && state.Blocks[BattleConst.BlockKnockOut] != 0)
        {
            return false;
        }

        BattleFighter? attacker = state.FighterFor(attackerSide);
        if (attacker is not { IsKnockedOut: false } || target is null || (requireLivingTarget && target.IsKnockedOut))
        {
            return false;
        }

        attacker.RefreshStats();
        target.RefreshStats();
        if (!BattleControlStatusRules.RollSucceeds(chance, attacker, target, Resist.KnockOut, elementResistance: 6, rng))
        {
            return false;
        }

        target.Hp = 0;
        state.CheckOutcome();
        state.ResolveEnemyReserve();
        return true;
    }
}
