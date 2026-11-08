namespace Dw3Production.Simulation;

/// <summary>Builds a fresh battle-stat snapshot from a fighter's mutable battle state.</summary>
internal static class BattleStatResolver
{
    /// <summary>Resolves the current stats without modifying the seed or the previously returned snapshot.</summary>
    /// <param name="fallback">Stats used when no partner source selects the current form.</param>
    /// <param name="seed">Partner state, or null for an enemy.</param>
    /// <param name="formId">Current Digimon form id.</param>
    /// <param name="flags">Current battle flags.</param>
    /// <param name="charge">Current charge in sixty-fourths.</param>
    /// <param name="boosts">Current attack, defense, speed and spirit boosts.</param>
    /// <returns>A fresh snapshot for the immediate rule calculation.</returns>
    public static BattleStats Resolve(
        BattleStats fallback,
        PartnerSeed? seed,
        int formId,
        int flags,
        int charge,
        int[] boosts)
    {
        BattleStats source = seed?.StatsFor(formId) ?? fallback;
        var result = Clone(source);
        result.Flags = flags;
        result.DamageBonus = charge;
        ApplyBoosts(result, boosts);
        return result;
    }

    /// <summary>Copies scalar fields and arrays so resolving one action cannot retain mutable state for another.</summary>
    /// <param name="source">Snapshot source.</param>
    /// <returns>A fully independent copy.</returns>
    private static BattleStats Clone(BattleStats source) => new()
    {
        Level = source.Level,
        Stats = [.. source.Stats],
        Resist = [.. source.Resist],
        Flags = source.Flags,
        Family = source.Family,
        DamageBonus = source.DamageBonus,
        DamageCut = source.DamageCut,
        WeaponFamilies = [.. source.WeaponFamilies],
        Element = source.Element,
        ElementPower = source.ElementPower,
        TripleHit = source.TripleHit,
        CriticalBonus = source.CriticalBonus,
        Counter = source.Counter,
        Accuracy = source.Accuracy,
        Evasion = source.Evasion,
        RunAwayBonus = source.RunAwayBonus,
        RunAwayGuard = source.RunAwayGuard,
        StealBonus = source.StealBonus,
    };

    /// <summary>Applies the battle-local boost ordering used by the original rules.</summary>
    /// <param name="stats">Stats receiving the modifiers.</param>
    /// <param name="boosts">Attack, defense, speed and spirit boosts.</param>
    private static void ApplyBoosts(BattleStats stats, int[] boosts)
    {
        ApplyBoost(stats, BattleStat.Attack, boosts, 0);
        ApplyBoost(stats, BattleStat.Defense, boosts, 1);
        ApplyBoost(stats, BattleStat.Speed, boosts, 2);
        ApplyBoost(stats, BattleStat.Spirit, boosts, 3);
    }

    /// <summary>Applies one source boost while retaining the EU minimum stat bound.</summary>
    /// <param name="stats">Stats receiving the boost.</param>
    /// <param name="stat">Target stat.</param>
    /// <param name="boosts">Source boost list.</param>
    /// <param name="index">Index in <paramref name="boosts"/>.</param>
    private static void ApplyBoost(BattleStats stats, int stat, int[] boosts, int index)
    {
        int boost = index < boosts.Length ? boosts[index] : 0;
        stats.Stats[stat] = Math.Max(stats.Stats[stat] + boost, 1);
    }
}
