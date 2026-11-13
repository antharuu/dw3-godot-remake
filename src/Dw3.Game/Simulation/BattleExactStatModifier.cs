namespace Dw3Production.Simulation;

/// <summary>Applies item rules that modify stats by exact resolved-stat units rather than percentages.</summary>
internal static class BattleExactStatModifier
{
    /// <summary>Applies a bounded delta and queues the matching normal item expiry.</summary>
    /// <param name="state">Battle state owning the target fighter.</param>
    /// <param name="scheduler">Scheduler that owns the temporary modifier expiry.</param>
    /// <param name="side">Side containing the target.</param>
    /// <param name="fighterSlot">Target fighter slot.</param>
    /// <param name="stat">Attack or defense stat being modified.</param>
    /// <param name="delta">Exact source-unit change.</param>
    /// <param name="rng">Shared random stream used for expiry timing.</param>
    /// <returns>Whether the bounded boost changed.</returns>
    public static bool Apply(BattleState state, BattleTurnScheduler scheduler, BattleSide side, int fighterSlot, int stat, int delta, IBattleRng rng)
    {
        int index = stat switch
        {
            BattleStat.Attack => 0,
            BattleStat.Defense => 1,
            _ => -1,
        };
        BattleFighter? fighter = state.FighterAt(side, fighterSlot);
        if (fighter is not { Id: not 0, IsKnockedOut: false } || index < 0 || delta == 0)
        {
            return false;
        }

        fighter.RefreshStats();
        int baseValue = Math.Max(fighter.Stats.Stats[stat] - fighter.Boosts[index], 1);
        int changed = Math.Clamp(fighter.Boosts[index] + delta, -(baseValue / 2), baseValue);
        if (changed == fighter.Boosts[index])
        {
            return false;
        }

        fighter.Boosts[index] = changed;
        fighter.RefreshStats();
        scheduler.EnqueueControlExpiry(index == 0 ? BattleScheduledEventKind.AttackBoostEnd : BattleScheduledEventKind.DefenseBoostEnd, side, fighterSlot, (fighter.Stats.Stats[BattleStat.Spirit] * 8) + 1000 + (rng.NextValue() % 2001));
        return true;
    }
}
