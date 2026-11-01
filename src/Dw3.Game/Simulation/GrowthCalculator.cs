namespace Dw3Production.Simulation;

/// <summary>Applies battle experience rewards to partner and Digimon progression values.</summary>
public static class GrowthCalculator
{
    /// <summary>The first levels of vital and experience tiers 1, 2 and 3 (tier 0 starts at level 1).</summary>
    private static readonly int[] VitalTierStarts = [5, 20, 40];

    /// <summary>The first levels of stat tiers 1 to 5, one per row of <see cref="GrowthTables.StatGains"/> after row 0.</summary>
    private static readonly int[] StatTierStarts = [5, 20, 40, 60, 80];

    /// <summary>Applies one level's stat increases to a partner state.</summary>
    public static void RaiseStats(GrowthTables tables, PartnerGrowthState state, PartnerGrowthCurve curve, int level, IBattleRng rng)
    {
        var vitalTier = TierOf(level, VitalTierStarts);
        state.MaxHp = CapVital(state.MaxHp + curve.HpGrowth - tables.VitalCuts[vitalTier] + tables.VitalRandom[rng.NextValue() % 9]);
        state.MaxMp = CapVital(state.MaxMp + curve.MpGrowth - tables.VitalCuts[vitalTier] + tables.VitalRandom[rng.NextValue() % 9]);

        var statTier = TierOf(level, StatTierStarts);
        for (var index = 0; index < state.BattleStats.Length; index++)
        {
            var gain = tables.StatGains[statTier][curve.StatGrowth[index] + (rng.NextValue() % 5)];
            state.BattleStats[index] = CapStat(state.BattleStats[index] + gain);
        }

        if (level >= 41)
        {
            return;
        }

        for (var index = 0; index < state.Resistances.Length; index++)
        {
            var gain = tables.ResistGains[curve.ResistGrowth[index] + (rng.NextValue() % 4)];
            state.Resistances[index] = CapStat(state.Resistances[index] + gain);
        }
    }

    /// <summary>Adds partner experience and applies every level reached by that experience.</summary>
    public static bool AddExp(GrowthTables tables, PartnerGrowthState state, PartnerGrowthCurve curve, int exp, IBattleRng rng)
    {
        state.Exp = Math.Min(state.Exp + exp, 999_999);

        var leveled = false;
        for (var level = state.Level + 1; level < 100; level++)
        {
            var tier = ExperienceTier(level);
            if (tier < 0)
            {
                break;
            }

            var threshold = (((level * level * level) + (level * 5) - 6) * curve.ExpRate / 10) + tables.LevelExp[tier];
            if (threshold >= state.Exp)
            {
                break;
            }

            LevelUp(tables, state, curve, rng);
            leveled = true;
        }

        return leveled;
    }

    /// <summary>Computes the experience share awarded to one participating Digimon.</summary>
    public static int GetDigimonExp(int level, int digimonExpLevel, int entryLevel, int exp, int used)
    {
        var tenfold = exp * 10;
        var amount = level < 51 ? tenfold / level : exp / 5;
        var share = used switch
        {
            0 or 1 => amount,
            2 => amount * 6 / 10,
            _ => amount / used,
        };

        if (share <= 0)
        {
            return 1;
        }

        return entryLevel < digimonExpLevel
            ? Math.Min(share, 10)
            : Math.Min(share, 50);
    }

    /// <summary>Adds Digimon experience and advances its entry level while thresholds are met.</summary>
    public static bool AddDigimonExp(int expLevel, ref int entryLevel, ref int entryExp, int exp)
    {
        entryExp += exp;
        if (entryExp > 9_999_999)
        {
            entryExp = 9_999_999;
        }

        if (entryLevel >= 99)
        {
            return false;
        }

        var leveled = false;
        while (entryLevel < 99)
        {
            var needed = expLevel >= entryLevel + 1
                ? entryLevel * 10
                : ((expLevel - 1) * 10) + ((entryLevel - (expLevel - 1)) * 50);
            if (entryExp < needed)
            {
                break;
            }

            entryLevel++;
            leveled = true;
        }

        return leveled;
    }

    /// <summary>Splits a partner experience reward according to the number of partners who fought.</summary>
    public static int ExpShare(int rewardExp, int fought) => fought switch
    {
        2 => rewardExp * 6 / 10,
        3 => rewardExp / 3,
        _ => rewardExp,
    };

    /// <summary>
    /// One level-up: the level rises (stats grow for levels 2..99, level 100 is clamped back to 99
    /// without growth) and 5 TP are added, capped at 99.
    /// </summary>
    /// <param name="tables">The growth lookup tables.</param>
    /// <param name="state">The partner to level up.</param>
    /// <param name="curve">The partner's Digimon growth profile.</param>
    /// <param name="rng">The battle's random source, for the stat swings.</param>
    private static void LevelUp(GrowthTables tables, PartnerGrowthState state, PartnerGrowthCurve curve, IBattleRng rng)
    {
        state.Level++;
        if (state.Level < 100)
        {
            RaiseStats(tables, state, curve, state.Level, rng);
        }
        else
        {
            state.Level = 99;
        }

        state.Tp = state.Tp + 5 >= 100 ? 99 : state.Tp + 5;
    }

    /// <summary>The experience tier of a level (the vital tiers), or -1 from level 100, which no curve reaches.</summary>
    /// <param name="level">The level being reached, 2..99 in practice.</param>
    /// <returns>The row of <see cref="GrowthTables.LevelExp"/>, or -1.</returns>
    private static int ExperienceTier(int level) => level < 100 ? TierOf(level, VitalTierStarts) : -1;

    /// <summary>Counts how many tier starts <paramref name="level"/> has reached; the starts are ascending.</summary>
    /// <param name="level">The partner level.</param>
    /// <param name="tierStarts">The first level of each tier after tier 0.</param>
    /// <returns>The tier index, 0..tierStarts.Length.</returns>
    private static int TierOf(int level, int[] tierStarts)
    {
        var tier = 0;
        while (tier < tierStarts.Length && level >= tierStarts[tier])
        {
            tier++;
        }

        return tier;
    }

    private static int CapVital(int value) => value >= 10_000 ? 9_999 : value;

    private static int CapStat(int value) => value >= 1_000 ? 999 : value;
}
