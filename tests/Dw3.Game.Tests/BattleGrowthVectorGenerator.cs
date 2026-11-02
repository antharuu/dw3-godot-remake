using System.Text.Json;
using System.Text.Json.Nodes;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Generates tests/Dw3.Game.Tests/Vectors/battle-growth.json when the
/// environment variable GENERATE_VECTORS=1 is set.  Run via:
///   GENERATE_VECTORS=1 dotnet test --filter "GenerateBattleGrowthVectors"
///
/// The generated file records fabricated (invented) inputs with the outputs
/// produced by the current GrowthCalculator implementation.  No game values
/// are embedded.
/// </summary>
public sealed class BattleGrowthVectorGenerator
{
    // ------------------------------------------------------------------ helpers

    private sealed class SeqRng : IBattleRng
    {
        private readonly int[] _v;
        private int _i;
        public SeqRng(params int[] v) => _v = v;
        // Returns 0 if the sequence is empty (safe fallback for cases with no level-up
        // that accidentally reach RNG due to zero thresholds or other edge conditions).
        public int NextValue() => _v.Length == 0 ? 0 : _v[_i++ % _v.Length];
    }

    /// Returns an int[] of length n filled with value v (for use as a SeqRng sequence).
    private static int[] ConstSeq(int v, int n)
    {
        var a = new int[n];
        for (int i = 0; i < n; i++) a[i] = v;
        return a;
    }

    private static GrowthTables MakeTables(
        int[] levelExp, int[] vitalCuts, int[] vitalRandom,
        int[][] statGains, int[] resistGains) => new()
        {
            LevelExp = levelExp,
            VitalCuts = vitalCuts,
            VitalRandom = vitalRandom,
            StatGains = statGains,
            ResistGains = resistGains,
        };

    private static int[][] MakeStatGains(params int[][] rows) => rows;

    private static GrowthTables DefaultTables() => MakeTables(
        levelExp: [0, 100, 500, 1000],
        vitalCuts: [5, 10, 15, 20],
        vitalRandom: [0, 2, 4, 6, 8, 10, 12, 14, 16],
        statGains: MakeStatGains(
            [1, 2, 3, 4, 5, 6, 7, 8, 9],
            [2, 3, 4, 5, 6, 7, 8, 9, 10],
            [3, 4, 5, 6, 7, 8, 9, 10, 11],
            [4, 5, 6, 7, 8, 9, 10, 11, 12],
            [5, 6, 7, 8, 9, 10, 11, 12, 13],
            [6, 7, 8, 9, 10, 11, 12, 13, 14]),
        resistGains: [0, 1, 2, 3, 4, 5, 6, 7]);

    // ------------------------------------------------------------------ JSON helpers

    private static JsonArray ToJsonArray(int[] arr)
    {
        var a = new JsonArray();
        foreach (var v in arr) a.Add(v);
        return a;
    }

    private static JsonArray StatGainsToJson(int[][] sg)
    {
        var outer = new JsonArray();
        foreach (var row in sg) outer.Add(ToJsonArray(row));
        return outer;
    }

    private static PartnerGrowthState CloneState(PartnerGrowthState s) => new()
    {
        Level = s.Level,
        Exp = s.Exp,
        Tp = s.Tp,
        MaxHp = s.MaxHp,
        MaxMp = s.MaxMp,
        BattleStats = (int[])s.BattleStats.Clone(),
        Resistances = (int[])s.Resistances.Clone(),
    };

    private static PartnerGrowthState MakeState(
        int level = 1, int exp = 0, int tp = 0,
        int maxHp = 100, int maxMp = 50,
        int[]? battleStats = null, int[]? resistances = null) => new()
        {
            Level = level,
            Exp = exp,
            Tp = tp,
            MaxHp = maxHp,
            MaxMp = maxMp,
            BattleStats = battleStats ?? [10, 10, 10, 10, 10, 10],
            Resistances = resistances ?? [100, 100, 100, 100, 100, 100, 100],
        };

    // ------------------------------------------------------------------ case builders

    /// RaiseStats: rngValues is the full materialized sequence (stored in JSON for replay).
    private static JsonObject RaiseStatsCase(
        int id, GrowthTables tables, PartnerGrowthState stateIn,
        PartnerGrowthCurve curve, int level, int[] rngValues)
    {
        var state = CloneState(stateIn);
        GrowthCalculator.RaiseStats(tables, state, curve, level, new SeqRng(rngValues));

        return new JsonObject
        {
            ["id"] = id,
            ["fn"] = "RaiseStats",
            ["level"] = level,
            ["hpGrowth"] = curve.HpGrowth,
            ["mpGrowth"] = curve.MpGrowth,
            ["statGrowth"] = ToJsonArray([.. curve.StatGrowth]),
            ["resistGrowth"] = ToJsonArray([.. curve.ResistGrowth]),
            ["rngSequence"] = ToJsonArray(rngValues),
            ["inMaxHp"] = stateIn.MaxHp,
            ["inMaxMp"] = stateIn.MaxMp,
            ["inBattleStats"] = ToJsonArray(stateIn.BattleStats),
            ["inResistances"] = ToJsonArray(stateIn.Resistances),
            ["outMaxHp"] = state.MaxHp,
            ["outMaxMp"] = state.MaxMp,
            ["outBattleStats"] = ToJsonArray(state.BattleStats),
            ["outResistances"] = ToJsonArray(state.Resistances),
            ["tableVitalCuts"] = ToJsonArray(tables.VitalCuts),
            ["tableVitalRandom"] = ToJsonArray(tables.VitalRandom),
            ["tableStatGains"] = StatGainsToJson(tables.StatGains),
            ["tableResistGains"] = ToJsonArray(tables.ResistGains),
        };
    }

    /// AddExp: rngValues is the full materialized sequence for all level-ups.
    private static JsonObject AddExpCase(
        int id, GrowthTables tables, PartnerGrowthState stateIn,
        int expRate, int hpGrowth, int mpGrowth,
        int[] statGrowth, int[] resistGrowth, int exp, int[] rngValues)
    {
        var state = CloneState(stateIn);
        bool leveled = GrowthCalculator.AddExp(tables, state,
            new PartnerGrowthCurve(expRate, hpGrowth, mpGrowth, statGrowth, resistGrowth), exp,
            new SeqRng(rngValues));

        return new JsonObject
        {
            ["id"] = id,
            ["fn"] = "AddExp",
            ["expRate"] = expRate,
            ["hpGrowth"] = hpGrowth,
            ["mpGrowth"] = mpGrowth,
            ["statGrowth"] = ToJsonArray(statGrowth),
            ["resistGrowth"] = ToJsonArray(resistGrowth),
            ["exp"] = exp,
            ["rngSequence"] = ToJsonArray(rngValues),
            ["inLevel"] = stateIn.Level,
            ["inExp"] = stateIn.Exp,
            ["inTp"] = stateIn.Tp,
            ["inMaxHp"] = stateIn.MaxHp,
            ["inMaxMp"] = stateIn.MaxMp,
            ["inBattleStats"] = ToJsonArray(stateIn.BattleStats),
            ["inResistances"] = ToJsonArray(stateIn.Resistances),
            ["outLevel"] = state.Level,
            ["outExp"] = state.Exp,
            ["outTp"] = state.Tp,
            ["outMaxHp"] = state.MaxHp,
            ["outMaxMp"] = state.MaxMp,
            ["outBattleStats"] = ToJsonArray(state.BattleStats),
            ["outResistances"] = ToJsonArray(state.Resistances),
            ["outLeveled"] = leveled,
            ["tableLevelExp"] = ToJsonArray(tables.LevelExp),
            ["tableVitalCuts"] = ToJsonArray(tables.VitalCuts),
            ["tableVitalRandom"] = ToJsonArray(tables.VitalRandom),
            ["tableStatGains"] = StatGainsToJson(tables.StatGains),
            ["tableResistGains"] = ToJsonArray(tables.ResistGains),
        };
    }

    private static JsonObject GetDigimonExpCase(
        int id, int level, int digimonExpLevel, int entryLevel, int exp, int used)
    {
        int result = GrowthCalculator.GetDigimonExp(level, digimonExpLevel, entryLevel, exp, used);
        return new JsonObject
        {
            ["id"] = id,
            ["fn"] = "GetDigimonExp",
            ["level"] = level,
            ["digimonExpLevel"] = digimonExpLevel,
            ["entryLevel"] = entryLevel,
            ["exp"] = exp,
            ["used"] = used,
            ["expected"] = result,
        };
    }

    private static JsonObject AddDigimonExpCase(
        int id, int expLevel, int entryLevel, int entryExp, int exp)
    {
        int el = entryLevel, ee = entryExp;
        bool leveled = GrowthCalculator.AddDigimonExp(expLevel, ref el, ref ee, exp);
        return new JsonObject
        {
            ["id"] = id,
            ["fn"] = "AddDigimonExp",
            ["expLevel"] = expLevel,
            ["inEntryLevel"] = entryLevel,
            ["inEntryExp"] = entryExp,
            ["exp"] = exp,
            ["outEntryLevel"] = el,
            ["outEntryExp"] = ee,
            ["outLeveled"] = leveled,
        };
    }

    private static JsonObject ExpShareCase(int id, int rewardExp, int fought)
    {
        int result = GrowthCalculator.ExpShare(rewardExp, fought);
        return new JsonObject
        {
            ["id"] = id,
            ["fn"] = "ExpShare",
            ["rewardExp"] = rewardExp,
            ["fought"] = fought,
            ["expected"] = result,
        };
    }

    // ------------------------------------------------------------------ generator

    [Fact]
    public void GenerateBattleGrowthVectors()
    {
        if (Environment.GetEnvironmentVariable("GENERATE_VECTORS") != "1")
            return;

        var cases = new JsonArray();
        int id = 1;
        var def = DefaultTables();

        // RNG sequences: 15 values covers the maximum (level<41: 2 vital + 6 stats + 7 resists).
        // For level >= 41: only 8 values are consumed; the extra values in the sequence are harmless.
        int[] statGrowthBase = [0, 1, 2, 3, 2, 1];
        int[] resistGrowthBase = [0, 1, 2, 1, 0, 1, 2];

        // ---- RaiseStats: all level tier boundaries ----
        int[] raiseLevels = [1, 4, 5, 19, 20, 39, 40, 41, 59, 60, 79, 80, 99];

        foreach (var lv in raiseLevels)
        {
            // RNG all-zero
            cases.Add(RaiseStatsCase(id++, def,
                MakeState(level: lv),
                curve: new PartnerGrowthCurve(0, 30, 20, statGrowthBase, resistGrowthBase),
                level: lv, rngValues: ConstSeq(0, 15)));

            // RNG all max for % 9
            cases.Add(RaiseStatsCase(id++, def,
                MakeState(level: lv),
                curve: new PartnerGrowthCurve(0, 30, 20, statGrowthBase, resistGrowthBase),
                level: lv, rngValues: ConstSeq(8, 15)));

            // Mixed RNG sequence
            cases.Add(RaiseStatsCase(id++, def,
                MakeState(level: lv),
                curve: new PartnerGrowthCurve(0, 25, 15, [1, 2, 3, 2, 1, 0], [1, 2, 1, 0, 2, 1, 0]),
                level: lv, rngValues: [3, 6, 1, 2, 3, 4, 0, 1, 2, 0, 1, 2, 3, 1, 0]));
        }

        // RaiseStats: near-saturation
        cases.Add(RaiseStatsCase(id++, def,
            MakeState(maxHp: 9990, maxMp: 9990,
                battleStats: [995, 996, 997, 998, 999, 999],
                resistances: [998, 999, 999, 995, 996, 997, 998]),
            curve: new PartnerGrowthCurve(0, 50, 50, [4, 4, 4, 4, 4, 4], [4, 4, 4, 4, 4, 4, 4]),
            level: 1, rngValues: ConstSeq(8, 15)));

        // RaiseStats: negative gain (vitalCut > hpGrowth + swing)
        cases.Add(RaiseStatsCase(id++, def,
            MakeState(maxHp: 50, maxMp: 50),
            curve: new PartnerGrowthCurve(0, 3, 3, statGrowthBase, resistGrowthBase),
            level: 20, rngValues: ConstSeq(0, 15)));

        // RaiseStats: varied hpGrowth / mpGrowth
        foreach (var hg in new[] { 0, 10, 50, 100 })
        {
            cases.Add(RaiseStatsCase(id++, def,
                MakeState(),
                curve: new PartnerGrowthCurve(0, hg, hg / 2 + 1, statGrowthBase, resistGrowthBase),
                level: 10, rngValues: ConstSeq(4, 15)));
        }

        // RaiseStats: varied stat-tier levels for broader coverage
        foreach (var lv in new[] { 2, 8, 15, 25, 35, 45, 55, 65, 75, 85, 95 })
        {
            cases.Add(RaiseStatsCase(id++, def,
                MakeState(level: lv, maxHp: lv * 50, maxMp: lv * 30),
                curve: new PartnerGrowthCurve(0, 20 + lv / 10, 15 + lv / 10, [2, 2, 2, 2, 2, 2], [1, 1, 1, 1, 1, 1, 1]),
                level: lv, rngValues: ConstSeq(3, 15)));
        }

        // ---- AddExp cases ----
        // no level-up
        cases.Add(AddExpCase(id++, def,
            MakeState(level: 1, exp: 0), expRate: 10,
            hpGrowth: 30, mpGrowth: 20,
            statGrowth: statGrowthBase, resistGrowth: resistGrowthBase,
            exp: 5, rngValues: []));

        // exactly one level-up (level 1→2, threshold = (8+10-6)*10/10 + 0 = 12)
        cases.Add(AddExpCase(id++, def,
            MakeState(level: 1, exp: 0), expRate: 10,
            hpGrowth: 30, mpGrowth: 20,
            statGrowth: statGrowthBase, resistGrowth: resistGrowthBase,
            exp: 13, rngValues: ConstSeq(0, 15)));

        // two level-ups (thresholds 12 and 36)
        cases.Add(AddExpCase(id++, def,
            MakeState(level: 1, exp: 0), expRate: 10,
            hpGrowth: 30, mpGrowth: 20,
            statGrowth: statGrowthBase, resistGrowth: resistGrowthBase,
            exp: 40, rngValues: ConstSeq(2, 30)));

        // at level 99 → no level-up
        cases.Add(AddExpCase(id++, def,
            MakeState(level: 99, exp: 0), expRate: 10,
            hpGrowth: 30, mpGrowth: 20,
            statGrowth: statGrowthBase, resistGrowth: resistGrowthBase,
            exp: 9999, rngValues: []));

        // exp cap at 999999
        cases.Add(AddExpCase(id++, def,
            MakeState(level: 1, exp: 999990), expRate: 10,
            hpGrowth: 30, mpGrowth: 20,
            statGrowth: statGrowthBase, resistGrowth: resistGrowthBase,
            exp: 100, rngValues: ConstSeq(0, 60)));

        // expRate = 0 (no cubic component, threshold = LevelExp[tier])
        cases.Add(AddExpCase(id++, def,
            MakeState(level: 1, exp: 0), expRate: 0,
            hpGrowth: 30, mpGrowth: 20,
            statGrowth: statGrowthBase, resistGrowth: resistGrowthBase,
            exp: 5, rngValues: []));

        // expRate = 0 with large LevelExp[0]=5 → level-up at 6
        var tablesHighBase = MakeTables(
            [5, 100, 500, 1000],
            def.VitalCuts, def.VitalRandom, def.StatGains, def.ResistGains);
        cases.Add(AddExpCase(id++, tablesHighBase,
            MakeState(level: 1, exp: 0), expRate: 0,
            hpGrowth: 30, mpGrowth: 20,
            statGrowth: statGrowthBase, resistGrowth: resistGrowthBase,
            exp: 6, rngValues: ConstSeq(0, 15)));

        // tier boundary level-ups (4→5, 19→20, 39→40)
        int[] tpLevels = [4, 19, 39];
        foreach (var lv in tpLevels)
        {
            int nextLevel = lv + 1;
            int nextTier = nextLevel < 5 ? 0 : nextLevel < 20 ? 1 : nextLevel < 40 ? 2 : 3;
            int threshold = (nextLevel * nextLevel * nextLevel + nextLevel * 5 - 6) * 10 / 10
                            + def.LevelExp[nextTier];
            cases.Add(AddExpCase(id++, def,
                MakeState(level: lv, exp: 0), expRate: 10,
                hpGrowth: 30, mpGrowth: 20,
                statGrowth: statGrowthBase, resistGrowth: resistGrowthBase,
                exp: threshold + 1, rngValues: ConstSeq(1, 15)));
        }

        // Tp near cap
        cases.Add(AddExpCase(id++, def,
            MakeState(level: 1, exp: 0, tp: 95), expRate: 10,
            hpGrowth: 30, mpGrowth: 20,
            statGrowth: statGrowthBase, resistGrowth: resistGrowthBase,
            exp: 100, rngValues: ConstSeq(0, 60)));

        // multiple level-ups with mixed RNG
        cases.Add(AddExpCase(id++, def,
            MakeState(level: 5, exp: 0), expRate: 10,
            hpGrowth: 20, mpGrowth: 15,
            statGrowth: [2, 2, 2, 2, 2, 2], resistGrowth: [1, 1, 1, 1, 1, 1, 1],
            exp: 5000, rngValues: [
                3, 6, 2, 1, 3, 0, 2, 1, 2, 3, 1,
                4, 5, 3, 0, 1, 2, 4, 3, 1, 2, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
                2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            ]));

        // varied expRate
        foreach (var rate in new[] { 5, 15, 20, 50 })
        {
            cases.Add(AddExpCase(id++, def,
                MakeState(level: 1, exp: 0), expRate: rate,
                hpGrowth: 20, mpGrowth: 10,
                statGrowth: [1, 1, 1, 1, 1, 1], resistGrowth: [0, 0, 0, 0, 0, 0, 0],
                exp: 200, rngValues: ConstSeq(0, 60)));
        }

        // ---- GetDigimonExp cases ----
        // level < 51, entryLevel < digimonExpLevel (cap 10)
        foreach (var lv in new[] { 1, 10, 25, 50 })
        {
            foreach (var exp in new[] { 0, 1, 5, 50, 100, 500 })
            {
                cases.Add(GetDigimonExpCase(id++, lv, 10, 3, exp, 1));
                cases.Add(GetDigimonExpCase(id++, lv, 5, 5, exp, 1));
            }
        }
        // level >= 51
        foreach (var lv in new[] { 51, 60, 75, 99 })
        {
            foreach (var exp in new[] { 0, 5, 50, 500 })
            {
                cases.Add(GetDigimonExpCase(id++, lv, 10, 3, exp, 1));
                cases.Add(GetDigimonExpCase(id++, lv, 5, 7, exp, 1));
            }
        }
        // used variants
        foreach (var used in new[] { 0, 1, 2, 3, 4, 5 })
            cases.Add(GetDigimonExpCase(id++, 10, 5, 5, 100, used));
        foreach (var used in new[] { 0, 1, 2, 3, 4 })
            cases.Add(GetDigimonExpCase(id++, 10, 5, 3, 100, used));
        // level boundary at 50/51
        cases.Add(GetDigimonExpCase(id++, 50, 5, 5, 100, 1));
        cases.Add(GetDigimonExpCase(id++, 51, 5, 5, 100, 1));
        // exp = 0 → always clamp to 1
        foreach (var lv in new[] { 1, 50, 99 })
        {
            cases.Add(GetDigimonExpCase(id++, lv, 5, 5, 0, 1));
            cases.Add(GetDigimonExpCase(id++, lv, 5, 5, 0, 3));
        }
        // large exp
        foreach (var exp in new[] { 1000, 9999, 99999 })
        {
            cases.Add(GetDigimonExpCase(id++, 10, 5, 5, exp, 1));
            cases.Add(GetDigimonExpCase(id++, 10, 5, 3, exp, 1));
            cases.Add(GetDigimonExpCase(id++, 10, 5, 5, exp, 2));
            cases.Add(GetDigimonExpCase(id++, 10, 5, 5, exp, 4));
            cases.Add(GetDigimonExpCase(id++, 60, 5, 5, exp, 1));
        }
        // used=2 truncation: n*6/10
        foreach (var exp in new[] { 1, 2, 3, 5, 7, 10, 11, 15, 17 })
            cases.Add(GetDigimonExpCase(id++, 1, 5, 5, exp, 2));

        // ---- AddDigimonExp cases ----
        const int expLv = 5;
        // level 0: need=0 → always level up
        foreach (var exp in new[] { 0, 1, 5, 10, 100 })
            cases.Add(AddDigimonExpCase(id++, expLv, 0, 0, exp));
        // level 1, need=10
        foreach (var (ee, ex) in new[] { (0, 9), (0, 10), (0, 11), (9, 1), (9, 0) })
            cases.Add(AddDigimonExpCase(id++, expLv, 1, ee, ex));
        // level 2, need=20
        cases.Add(AddDigimonExpCase(id++, expLv, 2, 18, 2));
        cases.Add(AddDigimonExpCase(id++, expLv, 2, 19, 0));
        cases.Add(AddDigimonExpCase(id++, expLv, 2, 19, 1));
        // expLevel boundary: 4→5 (need=40) and 5→6 (need=90)
        cases.Add(AddDigimonExpCase(id++, expLv, 4, 38, 2));
        cases.Add(AddDigimonExpCase(id++, expLv, 4, 39, 2));
        cases.Add(AddDigimonExpCase(id++, expLv, 5, 88, 2));
        cases.Add(AddDigimonExpCase(id++, expLv, 5, 89, 2));
        cases.Add(AddDigimonExpCase(id++, expLv, 6, 138, 2));
        // multiple level-ups
        cases.Add(AddDigimonExpCase(id++, expLv, 1, 0, 200));
        cases.Add(AddDigimonExpCase(id++, expLv, 0, 0, 200));
        cases.Add(AddDigimonExpCase(id++, expLv, 3, 0, 100));
        // entryLevel = 99 → no level-up, exp still added
        cases.Add(AddDigimonExpCase(id++, expLv, 99, 0, 50));
        cases.Add(AddDigimonExpCase(id++, expLv, 99, 0, 0));
        // entryLevel = 98 → levels to 99, stops
        cases.Add(AddDigimonExpCase(id++, expLv, 98, 0, 99999));
        // various expLevel values
        foreach (var el in new[] { 1, 2, 3, 5, 10 })
        {
            cases.Add(AddDigimonExpCase(id++, el, 0, 0, 50));
            cases.Add(AddDigimonExpCase(id++, el, 3, 25, 15));
            cases.Add(AddDigimonExpCase(id++, el, 1, 5, 5));
        }
        // exp cap at 9999999
        cases.Add(AddDigimonExpCase(id++, expLv, 5, 9999990, 20));
        cases.Add(AddDigimonExpCase(id++, expLv, 5, 9999990, 100));
        // varied expLevel boundaries
        foreach (var el in new[] { 3, 7, 10, 20 })
        {
            cases.Add(AddDigimonExpCase(id++, el, el - 1, 0, (el - 1) * 10 + 1));
            cases.Add(AddDigimonExpCase(id++, el, el, 0, (el - 1) * 10 + 50 + 1));
        }

        // ---- ExpShare cases ----
        foreach (var exp in new[] { 0, 1, 10, 11, 100, 101, 999, 9999 })
        {
            foreach (var fought in new[] { 0, 1, 2, 3, 4, 5 })
                cases.Add(ExpShareCase(id++, exp, fought));
        }

        // Pad to reach 300 cases with GetDigimonExp variants
        while (cases.Count < 300)
        {
            int extra = cases.Count;
            int lv = 1 + extra % 50;
            int ex = 10 + extra % 100;
            int used = 1 + extra % 3;
            cases.Add(GetDigimonExpCase(id++, lv, 6, 3 + extra % 5, ex, used));
        }

        var root = new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["seed"] = 42,
                ["count"] = cases.Count,
                ["note"] = "Fabricated vectors generated from the current GrowthCalculator implementation. All inputs are invented; no game values are embedded.",
            },
            ["cases"] = cases,
        };

        // Write to the build output Vectors directory (same location as the test binary).
        var outPath = Path.Combine(
            Path.GetDirectoryName(typeof(BattleGrowthVectorGenerator).Assembly.Location)!,
            "Vectors", "battle-growth.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        Assert.True(cases.Count >= 300, $"Generated {cases.Count} vectors, need at least 300.");
    }
}
