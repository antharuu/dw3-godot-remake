using System.Diagnostics.CodeAnalysis;

namespace Dw3Production.Simulation;

/// <summary>Applies the source-defined active-partner battle items while refusing an invalid or no-benefit use before it consumes stock.</summary>
internal static class BattlePartnerItemEffects
{
    private const int GaugeMaximum = 999;

    /// <summary>Raises the active partner's speed by the item amount in 128ths, up to its base value.</summary>
    /// <param name="state">Battle state containing the active partner and scheduler.</param>
    /// <param name="item">Source-classified speed item.</param>
    /// <param name="rng">Shared battle random source for expiry timing.</param>
    /// <returns>Whether the speed boost changed the active partner.</returns>
    public static bool TryBoostSpeed(BattleState state, BattleItem item, IBattleRng rng) =>
        TryBoost(state, item, BattleStat.Speed, item.Amount, rng);

    /// <summary>Raises attack and lowers defense using the source item's paired 128th and 512th ratios.</summary>
    /// <param name="state">Battle state containing the active partner and scheduler.</param>
    /// <param name="item">Source-classified attack item.</param>
    /// <param name="rng">Shared battle random source for expiry timing.</param>
    /// <returns>Whether either source stat change affected the active partner.</returns>
    public static bool TryBoostAttack(BattleState state, BattleItem item, IBattleRng rng) =>
        TryTradeoff(state, item, BattleStat.Attack, BattleStat.Defense, rng);

    /// <summary>Raises defense and lowers attack using the source item's paired 128th and 512th ratios.</summary>
    /// <param name="state">Battle state containing the active partner and scheduler.</param>
    /// <param name="item">Source-classified defense item.</param>
    /// <param name="rng">Shared battle random source for expiry timing.</param>
    /// <returns>Whether either source stat change affected the active partner.</returns>
    public static bool TryBoostDefense(BattleState state, BattleItem item, IBattleRng rng) =>
        TryTradeoff(state, item, BattleStat.Defense, BattleStat.Attack, rng);

    /// <summary>Sets the active partner's special state and arms its established expiry only once per active window.</summary>
    /// <param name="state">Battle state containing the active partner and scheduler.</param>
    /// <param name="item">Source-classified special item.</param>
    /// <param name="rng">Shared battle random source for expiry timing.</param>
    /// <returns>Whether a non-special active partner entered its special state.</returns>
    public static bool TrySpecial(BattleState state, BattleItem item, IBattleRng rng)
    {
        if (!TryActivePartner(state, item, out BattleFighter? fighter) || fighter.Special)
        {
            return false;
        }

        return state.ApplySpecialStatus(BattleSide.Player, state.ActivePlayer, rng);
    }

    /// <summary>Fills the active partner's blast gauge by the source amount, capped at 999.</summary>
    /// <param name="state">Battle state containing the active partner.</param>
    /// <param name="item">Source-classified gauge item.</param>
    /// <returns>Whether the gauge increased.</returns>
    public static bool TryFillGauge(BattleState state, BattleItem item)
    {
        if (!TryActivePartner(state, item, out BattleFighter? fighter))
        {
            return false;
        }

        int filled = Math.Min(fighter.BlastGauge + item.Amount, GaugeMaximum);
        if (filled == fighter.BlastGauge)
        {
            return false;
        }

        fighter.BlastGauge = filled;
        return true;
    }

    /// <summary>Applies a single source stat increase after verifying the boost can change the active partner.</summary>
    /// <param name="state">Battle state containing the active partner and scheduler.</param>
    /// <param name="item">Source-classified stat item.</param>
    /// <param name="stat">Stat receiving the positive source delta.</param>
    /// <param name="amount">Source amount in 128ths.</param>
    /// <param name="rng">Shared battle random source for expiry timing.</param>
    /// <returns>Whether the qualified stat changed.</returns>
    private static bool TryBoost(BattleState state, BattleItem item, int stat, int amount, IBattleRng rng)
    {
        if (!TryActivePartner(state, item, out BattleFighter? fighter) || !WouldChange(fighter, stat, amount))
        {
            return false;
        }

        return state.ApplyStatModifier(BattleSide.Player, state.ActivePlayer, stat, amount, rng);
    }

    /// <summary>Applies one source raise plus its paired quarter-strength lowering after rejecting a no-change use.</summary>
    /// <param name="state">Battle state containing the active partner and scheduler.</param>
    /// <param name="item">Source-classified trade-off item.</param>
    /// <param name="raisedStat">Stat receiving the item amount in 128ths.</param>
    /// <param name="loweredStat">Stat receiving the item amount in 512ths.</param>
    /// <param name="rng">Shared battle random source for expiry timing.</param>
    /// <returns>Whether either source delta can change the active partner.</returns>
    private static bool TryTradeoff(BattleState state, BattleItem item, int raisedStat, int loweredStat, IBattleRng rng)
    {
        if (!TryActivePartner(state, item, out BattleFighter? fighter) || (!WouldChange(fighter, raisedStat, item.Amount) && !WouldChange(fighter, loweredStat, -(item.Amount / 4))))
        {
            return false;
        }

        _ = state.ApplyStatModifier(BattleSide.Player, state.ActivePlayer, raisedStat, item.Amount, rng);
        _ = state.ApplyStatModifier(BattleSide.Player, state.ActivePlayer, loweredStat, -(item.Amount / 4), rng);
        return true;
    }

    /// <summary>Tests whether the source modifier's clamped result differs from the active boost value.</summary>
    /// <param name="fighter">Living active partner being previewed.</param>
    /// <param name="stat">Stat receiving the prospective modifier.</param>
    /// <param name="amount">Prospective delta in 128ths.</param>
    /// <returns>Whether the modifier would change the stat's stored boost.</returns>
    private static bool WouldChange(BattleFighter fighter, int stat, int amount)
    {
        int index = BoostIndex(stat);
        fighter.RefreshStats();
        int baseValue = Math.Max(fighter.Stats.Stats[stat] - fighter.Boosts[index], 1);
        int changed = fighter.Boosts[index] + ((baseValue * amount) / 128);
        return Math.Clamp(changed, -(baseValue / 2), baseValue) != fighter.Boosts[index];
    }

    /// <summary>Resolves the active living partner for a partner-targeted item.</summary>
    /// <param name="state">Battle state supplying the active partner.</param>
    /// <param name="item">Item whose target must be a partner.</param>
    /// <param name="fighter">Resolved active partner when valid.</param>
    /// <returns>Whether the active partner is valid for the source item.</returns>
    private static bool TryActivePartner(BattleState state, BattleItem item, [NotNullWhen(true)] out BattleFighter? fighter)
    {
        fighter = state.ActivePlayerFighter;
        return item.Target == "partner" && fighter is { IsKnockedOut: false };
    }

    /// <summary>Maps an exposed stat to the corresponding active-partner boost slot.</summary>
    /// <param name="stat">Attack, defense, or speed stat.</param>
    /// <returns>Boost slot for the supported stat.</returns>
    private static int BoostIndex(int stat) => stat switch
    {
        BattleStat.Attack => 0,
        BattleStat.Defense => 1,
        BattleStat.Speed => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };
}
