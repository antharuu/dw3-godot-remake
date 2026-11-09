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
        if (attackerSide == BattleSide.Player && state.Blocks[BattleConst.BlockKnockOut] != 0)
        {
            return false;
        }

        BattleFighter? attacker = state.FighterFor(attackerSide);
        BattleSide targetSide = BattlePoisonRules.Opposite(attackerSide);
        BattleFighter? target = state.FighterFor(targetSide);
        if (attacker is not { IsKnockedOut: false } || target is not { IsKnockedOut: false })
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
