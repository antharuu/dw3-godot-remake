namespace Dw3Production.Simulation;

/// <summary>
/// Source-qualified rules for the blast digivolution gauge: gain per hit,
/// trigger threshold, tier selection and duration mapping.
/// </summary>
/// <remarks>
/// Qualified from B9-076 checkpoint 992: damage 22 → gain 2, gauge 999 → 1001 (clamped to 1000).
/// TierLevels [4, 19, 39, 70] select one of five durations; tier is the count of boundaries
/// that the partner's level meets or exceeds, mapping level 25 to tier 2 (3 s).
/// </remarks>
public static class BattleBlastGaugeRules
{
    /// <summary>Gets the gauge value at which the blast digivolution is triggered.</summary>
    public const int GaugeThreshold = 1000;

    /// <summary>Computes the gauge gain from a single player action's total damage.</summary>
    /// <param name="damage">Total HP removed from the enemy by the action (never negative).</param>
    /// <returns>Integer gain added to the gauge, zero when damage is zero.</returns>
    public static int ComputeGain(int damage) => damage / 10;

    /// <summary>Clamps a raw gauge total so it never exceeds the threshold.</summary>
    /// <param name="gauge">Raw value after adding gain.</param>
    /// <returns>Value in [0, <see cref="GaugeThreshold"/>].</returns>
    public static int ClampGauge(int gauge) => Math.Min(gauge, GaugeThreshold);

    /// <summary>Returns whether a gauge change crosses the trigger threshold for the first time.</summary>
    /// <param name="previousGauge">Gauge before the last gain was added.</param>
    /// <param name="newGauge">Gauge after gain was added and clamped.</param>
    /// <returns><see langword="true"/> when the previous value was below the threshold and the new value reaches it.</returns>
    public static bool IsTriggered(int previousGauge, int newGauge) =>
        previousGauge < GaugeThreshold && newGauge >= GaugeThreshold;

    /// <summary>Selects the blast tier from the partner's level and the tier-boundary table.</summary>
    /// <param name="tierLevels">Ordered level boundaries from <see cref="Dw3Production.Conversion.DigivolutionBlast"/>.</param>
    /// <param name="level">Partner's current battle level.</param>
    /// <returns>Tier index (0 = lowest, count = highest), always in [0, tierLevels.Count].</returns>
    public static int SelectTier(IReadOnlyList<int> tierLevels, int level)
    {
        ArgumentNullException.ThrowIfNull(tierLevels);
        return tierLevels.Count(boundary => level >= boundary);
    }

    /// <summary>Maps a blast tier to its scheduled return delay kind.</summary>
    /// <param name="tier">Tier returned by <see cref="SelectTier"/>.</param>
    /// <returns>Blast delay family for the return scheduler.</returns>
    public static BattleActionDelayKind DelayKindForTier(int tier) => tier switch
    {
        0 => BattleActionDelayKind.BlastTwoSeconds,
        1 => BattleActionDelayKind.BlastTwoAndHalfSeconds,
        2 => BattleActionDelayKind.BlastThreeSeconds,
        3 => BattleActionDelayKind.BlastThreeAndHalfSeconds,
        _ => BattleActionDelayKind.BlastFourSeconds,
    };
}
