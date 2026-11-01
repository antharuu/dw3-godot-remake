namespace Dw3Production.Simulation;

/// <summary>Contains the experience and money awarded by a completed battle.</summary>
public readonly record struct BattleReward(int DigimonExp, int Exp, int Money);

/// <summary>Holds the lookup values used to grow a partner after battle experience.</summary>
public sealed class GrowthTables
{
    /// <summary>Gets or sets the cumulative experience base for each level tier.</summary>
    public int[] LevelExp { get; set; } = new int[4];

    /// <summary>Gets or sets the HP and MP reduction for each vital tier.</summary>
    public int[] VitalCuts { get; set; } = new int[4];

    /// <summary>Gets or sets the random HP and MP gain adjustments.</summary>
    public int[] VitalRandom { get; set; } = new int[9];

    /// <summary>Gets or sets the per-tier stat gain lookup rows.</summary>
    public int[][] StatGains { get; set; } = CreateStatGainRows();

    /// <summary>Gets or sets the resistance gain lookup values.</summary>
    public int[] ResistGains { get; set; } = new int[8];

    private static int[][] CreateStatGainRows()
    {
        var rows = new int[6][];
        for (var index = 0; index < rows.Length; index++)
            rows[index] = new int[9];
        return rows;
    }
}

/// <summary>Stores the persistent battle-growth values for one partner.</summary>
public sealed class PartnerGrowthState
{
    /// <summary>Gets or sets the current partner level.</summary>
    public int Level { get; set; } = 1;

    /// <summary>Gets or sets the cumulative partner experience.</summary>
    public int Exp { get; set; }

    /// <summary>Gets or sets the available technique points.</summary>
    public int Tp { get; set; }

    /// <summary>Gets or sets the maximum HP.</summary>
    public int MaxHp { get; set; }

    /// <summary>Gets or sets the maximum MP.</summary>
    public int MaxMp { get; set; }

    /// <summary>Gets or sets the six battle stats in their fixed battle order.</summary>
    public int[] BattleStats { get; set; } = new int[6];

    /// <summary>Gets or sets the seven elemental resistances.</summary>
    public int[] Resistances { get; set; } = new int[7];
}

/// <summary>Applies battle experience rewards to partner and Digimon progression values.</summary>
public static class GrowthCalculator
{
    /// <summary>Applies one level's stat increases to a partner state.</summary>
    public static void RaiseStats(
        GrowthTables tables,
        PartnerGrowthState state,
        int hpGrowth,
        int mpGrowth,
        IReadOnlyList<int> statGrowth,
        IReadOnlyList<int> resistGrowth,
        int level,
        BattleRng rng)
    {
        var vitalTier = VitalTier(level);
        state.MaxHp = CapVital(state.MaxHp + hpGrowth - tables.VitalCuts[vitalTier] + tables.VitalRandom[rng.Next() % 9]);
        state.MaxMp = CapVital(state.MaxMp + mpGrowth - tables.VitalCuts[vitalTier] + tables.VitalRandom[rng.Next() % 9]);

        var statTier = StatTier(level);
        for (var index = 0; index < state.BattleStats.Length; index++)
        {
            var gain = tables.StatGains[statTier][statGrowth[index] + rng.Next() % 5];
            state.BattleStats[index] = CapStat(state.BattleStats[index] + gain);
        }

        if (level >= 41)
            return;

        for (var index = 0; index < state.Resistances.Length; index++)
        {
            var gain = tables.ResistGains[resistGrowth[index] + rng.Next() % 4];
            state.Resistances[index] = CapStat(state.Resistances[index] + gain);
        }
    }

    /// <summary>Adds partner experience and applies every level reached by that experience.</summary>
    public static bool AddExp(
        GrowthTables tables,
        PartnerGrowthState state,
        int expRate,
        int hpGrowth,
        int mpGrowth,
        IReadOnlyList<int> statGrowth,
        IReadOnlyList<int> resistGrowth,
        int exp,
        BattleRng rng)
    {
        state.Exp += exp;
        if (state.Exp > 999_999)
            state.Exp = 999_999;

        var leveled = false;
        for (var level = state.Level + 1; level < 100; level++)
        {
            var tier = ExperienceTier(level);
            if (tier < 0)
                break;

            var threshold = (level * level * level + level * 5 - 6) * expRate / 10 + tables.LevelExp[tier];
            if (threshold >= state.Exp)
                break;

            state.Level++;
            if (state.Level < 100)
                RaiseStats(tables, state, hpGrowth, mpGrowth, statGrowth, resistGrowth, state.Level, rng);
            else
                state.Level = 99;

            state.Tp += 5;
            if (state.Tp >= 100)
                state.Tp = 99;
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
            return 1;

        return entryLevel < digimonExpLevel
            ? Math.Min(share, 10)
            : Math.Min(share, 50);
    }

    /// <summary>Adds Digimon experience and advances its entry level while thresholds are met.</summary>
    public static bool AddDigimonExp(int expLevel, ref int entryLevel, ref int entryExp, int exp)
    {
        entryExp += exp;
        if (entryExp > 9_999_999)
            entryExp = 9_999_999;

        if (entryLevel >= 99)
            return false;

        var leveled = false;
        while (entryLevel < 99)
        {
            var needed = expLevel >= entryLevel + 1
                ? entryLevel * 10
                : (expLevel - 1) * 10 + (entryLevel - (expLevel - 1)) * 50;
            if (entryExp < needed)
                break;

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

    private static int VitalTier(int level) => level < 5 ? 0 : level < 20 ? 1 : level < 40 ? 2 : 3;

    private static int StatTier(int level) => level < 5 ? 0 : level < 20 ? 1 : level < 40 ? 2 : level < 60 ? 3 : level < 80 ? 4 : 5;

    private static int ExperienceTier(int level) => level < 5 ? 0 : level < 20 ? 1 : level < 40 ? 2 : level < 100 ? 3 : -1;

    private static int CapVital(int value) => value >= 10_000 ? 9_999 : value;

    private static int CapStat(int value) => value >= 1_000 ? 999 : value;
}
