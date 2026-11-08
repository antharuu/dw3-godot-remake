namespace Dw3Production.Simulation;

/// <summary>Calculates application, duration and turn-control rules for paralysis, confusion and sleep.</summary>
public static class BattleControlStatusRules
{
    /// <summary>Tests one control-status application against its paired status and elemental resistances.</summary>
    /// <param name="baseChance">Technique or passive base chance.</param>
    /// <param name="attacker">Fighter applying the status.</param>
    /// <param name="target">Fighter resisting the status.</param>
    /// <param name="statusResistance">Named status-resistance slot.</param>
    /// <param name="elementResistance">Paired elemental-resistance slot.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether the application roll succeeds.</returns>
    public static bool RollSucceeds(int baseChance, BattleFighter attacker, BattleFighter target, int statusResistance, int elementResistance, IBattleRng rng)
    {
        int resistance = target.Stats.Resist[statusResistance] + target.Stats.Resist[elementResistance];
        int chance = baseChance + (attacker.Stats.Stats[BattleStat.Wisdom] / 8) - ((resistance + (target.Stats.Stats[BattleStat.Wisdom] / 2)) / 8);
        return (rng.NextValue() & 0x7F) < Math.Clamp(chance, 1, 127);
    }

    /// <summary>Calculates the source-compatible expiry delay of a status applied by a technique.</summary>
    /// <param name="kind">Control status being applied.</param>
    /// <param name="attacker">Fighter applying the status.</param>
    /// <param name="target">Fighter receiving the status.</param>
    /// <param name="strength">Stored status strength.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Non-negative battle-time delay until expiry.</returns>
    public static int ExpiryDelay(BattleScheduledEventKind kind, BattleFighter attacker, BattleFighter target, int strength, IBattleRng rng)
    {
        int elementalResistance = kind switch
        {
            BattleScheduledEventKind.ParalysisEnd => 4,
            BattleScheduledEventKind.ConfusionEnd => 3,
            BattleScheduledEventKind.SleepEnd => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Expected a control-status expiry."),
        };
        int statusResistance = kind switch
        {
            BattleScheduledEventKind.ParalysisEnd => Resist.Paralysis,
            BattleScheduledEventKind.ConfusionEnd => Resist.Confusion,
            BattleScheduledEventKind.SleepEnd => Resist.Sleep,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Expected a control-status expiry."),
        };
        int baseDelay = kind == BattleScheduledEventKind.SleepEnd ? 1000 : 3000;
        int delay = (rng.NextValue() % 500) + baseDelay + ((attacker.Stats.Stats[BattleStat.Spirit] + strength) * 8)
            - ((target.Stats.Resist[statusResistance] + target.Stats.Resist[elementalResistance]) * 8);
        return Math.Max(delay, kind == BattleScheduledEventKind.SleepEnd ? 500 : 1000);
    }

    /// <summary>Tests whether paralysis consumes the current fighter's turn.</summary>
    /// <param name="fighter">Paralyzed fighter.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether the fighter loses its turn.</returns>
    public static bool LosesTurnToParalysis(BattleFighter fighter, IBattleRng rng)
    {
        int chance = fighter.Paralysis - ((fighter.Stats.Resist[Resist.Paralysis] + fighter.Stats.Resist[4]) / 8);
        return (rng.NextValue() & 0x7F) < Math.Max(chance, 32);
    }

    /// <summary>Tests whether a damaging hit wakes a sleeping fighter.</summary>
    /// <param name="fighter">Sleeping fighter receiving the hit.</param>
    /// <param name="damage">Resolved damage value.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether sleep ends.</returns>
    public static bool WakesFromHit(BattleFighter fighter, int damage, IBattleRng rng)
    {
        int defense = Math.Max(fighter.Stats.Stats[BattleStat.Defense], 1);
        int chance = ((damage << 7) / defense) + 64 - (fighter.Sleep >> 1);
        return (rng.NextValue() & 0x7F) < chance;
    }
}
