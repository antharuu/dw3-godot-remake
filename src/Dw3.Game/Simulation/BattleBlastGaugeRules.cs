namespace Dw3Production.Simulation;

/// <summary>Integer rules for received-damage Blast charge and permanent-partner form selection.</summary>
public static class BattleBlastGaugeRules
{
    /// <summary>Gauge threshold that requests one temporary transformation.</summary>
    public const int GaugeThreshold = 1000;

    /// <summary>Computes HP-relative charge, applying each accessory to the running integer gain.</summary>
    /// <param name="damage">Qualified damage reported by the completed enemy action.</param>
    /// <param name="maxHp">Maximum HP of the receiving partner.</param>
    /// <param name="accessories">Two accessory slots in their equipped order.</param>
    /// <returns>Non-negative charge capped at the gauge threshold.</returns>
    public static int ComputeGain(int damage, int maxHp, IReadOnlyList<int>? accessories = null)
    {
        if (damage <= 0 || maxHp <= 0)
        {
            return 0;
        }

        long percent = Math.Min((long)damage * 100 / maxHp, 1000);
        long gain = percent * percent / 20;
        foreach (int accessory in (accessories ?? []).Take(2))
        {
            gain += accessory switch
            {
                0x149 => gain / 5,
                0x14A => gain * 4 / 10,
                _ => 0,
            };
        }

        return (int)Math.Min(gain, GaugeThreshold);
    }

    /// <summary>Clamps persistent gauge values to the supported range.</summary>
    /// <param name="gauge">Raw gauge value.</param>
    /// <returns>A value between zero and the trigger threshold.</returns>
    public static int ClampGauge(int gauge) => Math.Clamp(gauge, 0, GaugeThreshold);

    /// <summary>Tests whether the last gain reached the threshold for the first time.</summary>
    /// <param name="previousGauge">Gauge before charging.</param>
    /// <param name="newGauge">Gauge after charging.</param>
    /// <returns>Whether the threshold was crossed.</returns>
    public static bool IsTriggered(int previousGauge, int newGauge) => previousGauge < GaugeThreshold && newGauge >= GaugeThreshold;

    /// <summary>Selects a form tier from the permanent partner level, not the active form level.</summary>
    /// <param name="tierLevels">Ascending exclusive upper bounds of the first tiers.</param>
    /// <param name="level">Permanent partner level.</param>
    /// <returns>The selected form tier.</returns>
    public static int SelectTier(IReadOnlyList<int> tierLevels, int level)
    {
        ArgumentNullException.ThrowIfNull(tierLevels);
        return tierLevels.Count(boundary => level >= boundary);
    }

    /// <summary>Selects the return family by permanent partner ordinal rather than form tier.</summary>
    /// <param name="partner">Zero-based permanent partner ordinal.</param>
    /// <returns>The corresponding event-delay family.</returns>
    public static BattleActionDelayKind DelayKindForPartner(int partner) => partner switch
    {
        0 => BattleActionDelayKind.BlastTwoSeconds,
        1 => BattleActionDelayKind.BlastTwoAndHalfSeconds,
        2 => BattleActionDelayKind.BlastThreeSeconds,
        3 => BattleActionDelayKind.BlastThreeAndHalfSeconds,
        4 => BattleActionDelayKind.BlastFourSeconds,
        5 => BattleActionDelayKind.RandomStatusExpiry,
        6 => BattleActionDelayKind.Paralysis,
        7 => BattleActionDelayKind.Confusion,
        _ => throw new ArgumentOutOfRangeException(nameof(partner)),
    };
}
