using System.Text.Json.Nodes;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Differential regression suite for GrowthCalculator (battle-growth module).
/// Reads 300+ fabricated test vectors from Vectors/battle-growth.json,
/// replays each against the current implementation, and asserts the output
/// matches the recorded expected value.
///
/// Vectors were generated from the current implementation with seed 42 and
/// cover boundary values, edge cases, and systematic inputs for every public
/// method.  No game values are embedded: all inputs are invented.
/// </summary>
public sealed class BattleGrowthVectorTests
{
    private static readonly string VectorsPath = Path.Combine(
        AppContext.BaseDirectory, "Vectors", "battle-growth.json");

    // ------------------------------------------------------------------ count check
    [Fact]
    public void VectorCount_AtLeast300()
    {
        Assert.True(
            File.Exists(VectorsPath),
            $"Vectors file not found: {VectorsPath}");

        var doc = JsonNode.Parse(File.ReadAllText(VectorsPath))!;
        int count = doc["cases"]!.AsArray().Count;
        Assert.True(
            count >= 300,
            $"Expected at least 300 vectors, got {count}");
    }

    // ------------------------------------------------------------------ main replay
    [Fact]
    public void AllVectorsMatchCurrentImplementation()
    {
        Assert.True(
            File.Exists(VectorsPath),
            $"Vectors file not found: {VectorsPath}");

        var doc = JsonNode.Parse(File.ReadAllText(VectorsPath))!;
        var cases = doc["cases"]!.AsArray();
        var errors = new List<string>();

        foreach (var c in cases)
        {
            int id = c!["id"]!.GetValue<int>();
            string fn = c["fn"]!.GetValue<string>();

            try
            {
                switch (fn)
                {
                    case "RaiseStats":
                        VerifyRaiseStats(c, id, errors);
                        break;

                    case "AddExp":
                        VerifyAddExp(c, id, errors);
                        break;

                    case "GetDigimonExp":
                        VerifyGetDigimonExp(c, id, errors);
                        break;

                    case "AddDigimonExp":
                        VerifyAddDigimonExp(c, id, errors);
                        break;

                    case "ExpShare":
                        VerifyExpShare(c, id, errors);
                        break;

                    default:
                        errors.Add($"[{id}] Unknown fn '{fn}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"[{id}] {fn} threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            Assert.Fail($"{errors.Count} vector(s) failed:\n" + string.Join("\n", errors));
        }
    }

    private static int[] ToIntArray(JsonNode? node)
    {
        var arr = node!.AsArray();
        var r = new int[arr.Count];
        for (int i = 0; i < arr.Count; i++)
        {
            r[i] = arr[i]!.GetValue<int>();
        }

        return r;
    }

    private static int[][] ToInt2DArray(JsonNode? node)
    {
        var outer = node!.AsArray();
        var r = new int[outer.Count][];
        for (int i = 0; i < outer.Count; i++)
        {
            r[i] = ToIntArray(outer[i]);
        }

        return r;
    }

    private static GrowthTables TablesFromCase(JsonNode c) => new()
    {
        LevelExp = c["tableLevelExp"] is JsonNode le ? ToIntArray(le) : new int[4],
        VitalCuts = ToIntArray(c["tableVitalCuts"]),
        VitalRandom = ToIntArray(c["tableVitalRandom"]),
        StatGains = ToInt2DArray(c["tableStatGains"]),
        ResistGains = ToIntArray(c["tableResistGains"]),
    };

    private static GrowthTables TablesWithLevelExpFromCase(JsonNode c)
    {
        // AddExp cases embed LevelExp separately
        return new GrowthTables
        {
            LevelExp = c["tableLevelExp"] != null ? ToIntArray(c["tableLevelExp"]) : new int[4],
            VitalCuts = ToIntArray(c["tableVitalCuts"]),
            VitalRandom = ToIntArray(c["tableVitalRandom"]),
            StatGains = ToInt2DArray(c["tableStatGains"]),
            ResistGains = ToIntArray(c["tableResistGains"]),
        };
    }

    // ------------------------------------------------------------------ per-method verifiers
    private static void VerifyRaiseStats(JsonNode c, int id, List<string> errors)
    {
        var tables = TablesFromCase(c);

        var state = new PartnerGrowthState
        {
            MaxHp = c["inMaxHp"]!.GetValue<int>(),
            MaxMp = c["inMaxMp"]!.GetValue<int>(),
            BattleStats = ToIntArray(c["inBattleStats"]),
            Resistances = ToIntArray(c["inResistances"]),
        };

        int hpGrowth = c["hpGrowth"]!.GetValue<int>();
        int mpGrowth = c["mpGrowth"]!.GetValue<int>();
        int[] statGrowth = ToIntArray(c["statGrowth"]);
        int[] resistGrowth = ToIntArray(c["resistGrowth"]);
        int level = c["level"]!.GetValue<int>();
        int[] rngSeq = ToIntArray(c["rngSequence"]);
        IBattleRng rng = rngSeq.Length > 0 ? new SeqRng(rngSeq) : new ConstRng(0);

        GrowthCalculator.RaiseStats(tables, state, new PartnerGrowthCurve(0, hpGrowth, mpGrowth, statGrowth, resistGrowth), level, rng);

        int expMaxHp = c["outMaxHp"]!.GetValue<int>();
        int expMaxMp = c["outMaxMp"]!.GetValue<int>();
        int[] expBs = ToIntArray(c["outBattleStats"]);
        int[] expRes = ToIntArray(c["outResistances"]);

        if (state.MaxHp != expMaxHp)
        {
            errors.Add($"[{id}] RaiseStats MaxHp: got {state.MaxHp} expected {expMaxHp}");
        }

        if (state.MaxMp != expMaxMp)
        {
            errors.Add($"[{id}] RaiseStats MaxMp: got {state.MaxMp} expected {expMaxMp}");
        }

        for (int i = 0; i < 6; i++)
        {
            if (state.BattleStats[i] != expBs[i])
            {
                errors.Add($"[{id}] RaiseStats BattleStats[{i}]: got {state.BattleStats[i]} expected {expBs[i]}");
            }
        }

        for (int i = 0; i < 7; i++)
        {
            if (state.Resistances[i] != expRes[i])
            {
                errors.Add($"[{id}] RaiseStats Resistances[{i}]: got {state.Resistances[i]} expected {expRes[i]}");
            }
        }
    }

    private static void VerifyAddExp(JsonNode c, int id, List<string> errors)
    {
        var tables = TablesWithLevelExpFromCase(c);

        var state = new PartnerGrowthState
        {
            Level = c["inLevel"]!.GetValue<int>(),
            Exp = c["inExp"]!.GetValue<int>(),
            Tp = c["inTp"]!.GetValue<int>(),
            MaxHp = c["inMaxHp"]!.GetValue<int>(),
            MaxMp = c["inMaxMp"]!.GetValue<int>(),
            BattleStats = ToIntArray(c["inBattleStats"]),
            Resistances = ToIntArray(c["inResistances"]),
        };

        int expRate = c["expRate"]!.GetValue<int>();
        int hpGrowth = c["hpGrowth"]!.GetValue<int>();
        int mpGrowth = c["mpGrowth"]!.GetValue<int>();
        int[] statGrowth = ToIntArray(c["statGrowth"]);
        int[] resistGrowth = ToIntArray(c["resistGrowth"]);
        int exp = c["exp"]!.GetValue<int>();
        int[] rngSeq = ToIntArray(c["rngSequence"]);
        IBattleRng rng = rngSeq.Length > 0 ? new SeqRng(rngSeq) : new ConstRng(0);

        bool leveled = GrowthCalculator.AddExp(tables, state, new PartnerGrowthCurve(expRate, hpGrowth, mpGrowth, statGrowth, resistGrowth), exp, rng);

        bool expLeveled = c["outLeveled"]!.GetValue<bool>();
        int expLevel = c["outLevel"]!.GetValue<int>();
        int expExp = c["outExp"]!.GetValue<int>();
        int expTp = c["outTp"]!.GetValue<int>();
        int expMaxHp = c["outMaxHp"]!.GetValue<int>();
        int expMaxMp = c["outMaxMp"]!.GetValue<int>();
        int[] expBs = ToIntArray(c["outBattleStats"]);
        int[] expRes = ToIntArray(c["outResistances"]);

        if (leveled != expLeveled)
        {
            errors.Add($"[{id}] AddExp leveled: got {leveled} expected {expLeveled}");
        }

        if (state.Level != expLevel)
        {
            errors.Add($"[{id}] AddExp Level: got {state.Level} expected {expLevel}");
        }

        if (state.Exp != expExp)
        {
            errors.Add($"[{id}] AddExp Exp: got {state.Exp} expected {expExp}");
        }

        if (state.Tp != expTp)
        {
            errors.Add($"[{id}] AddExp Tp: got {state.Tp} expected {expTp}");
        }

        if (state.MaxHp != expMaxHp)
        {
            errors.Add($"[{id}] AddExp MaxHp: got {state.MaxHp} expected {expMaxHp}");
        }

        if (state.MaxMp != expMaxMp)
        {
            errors.Add($"[{id}] AddExp MaxMp: got {state.MaxMp} expected {expMaxMp}");
        }

        for (int i = 0; i < 6; i++)
        {
            if (state.BattleStats[i] != expBs[i])
            {
                errors.Add($"[{id}] AddExp BattleStats[{i}]: got {state.BattleStats[i]} expected {expBs[i]}");
            }
        }

        for (int i = 0; i < 7; i++)
        {
            if (state.Resistances[i] != expRes[i])
            {
                errors.Add($"[{id}] AddExp Resistances[{i}]: got {state.Resistances[i]} expected {expRes[i]}");
            }
        }
    }

    private static void VerifyGetDigimonExp(JsonNode c, int id, List<string> errors)
    {
        int level = c["level"]!.GetValue<int>();
        int digimonExpLevel = c["digimonExpLevel"]!.GetValue<int>();
        int entryLevel = c["entryLevel"]!.GetValue<int>();
        int exp = c["exp"]!.GetValue<int>();
        int used = c["used"]!.GetValue<int>();
        int expected = c["expected"]!.GetValue<int>();

        int got = GrowthCalculator.GetDigimonExp(level, digimonExpLevel, entryLevel, exp, used);
        if (got != expected)
        {
            errors.Add($"[{id}] GetDigimonExp(lv={level},expLv={digimonExpLevel},entry={entryLevel},exp={exp},used={used}): got {got} expected {expected}");
        }
    }

    private static void VerifyAddDigimonExp(JsonNode c, int id, List<string> errors)
    {
        int expLevel = c["expLevel"]!.GetValue<int>();
        int entryLevel = c["inEntryLevel"]!.GetValue<int>();
        int entryExp = c["inEntryExp"]!.GetValue<int>();
        int exp = c["exp"]!.GetValue<int>();
        int expEntryLevel = c["outEntryLevel"]!.GetValue<int>();
        int expEntryExp = c["outEntryExp"]!.GetValue<int>();
        bool expLeveled = c["outLeveled"]!.GetValue<bool>();

        bool leveled = GrowthCalculator.AddDigimonExp(expLevel, ref entryLevel, ref entryExp, exp);

        if (leveled != expLeveled)
        {
            errors.Add($"[{id}] AddDigimonExp leveled: got {leveled} expected {expLeveled}");
        }

        if (entryLevel != expEntryLevel)
        {
            errors.Add($"[{id}] AddDigimonExp entryLevel: got {entryLevel} expected {expEntryLevel}");
        }

        if (entryExp != expEntryExp)
        {
            errors.Add($"[{id}] AddDigimonExp entryExp: got {entryExp} expected {expEntryExp}");
        }
    }

    private static void VerifyExpShare(JsonNode c, int id, List<string> errors)
    {
        int rewardExp = c["rewardExp"]!.GetValue<int>();
        int fought = c["fought"]!.GetValue<int>();
        int expected = c["expected"]!.GetValue<int>();

        int got = GrowthCalculator.ExpShare(rewardExp, fought);
        if (got != expected)
        {
            errors.Add($"[{id}] ExpShare(exp={rewardExp},fought={fought}): got {got} expected {expected}");
        }
    }

    // ------------------------------------------------------------------ helpers
    private sealed class SeqRng : IBattleRng
    {
        private readonly int[] _v;
        private int _i;

        public SeqRng(params int[] v) => _v = v;

        // Returns 0 if the sequence is empty (safe fallback).
        public int NextValue() => _v.Length == 0 ? 0 : _v[_i++ % _v.Length];
    }

    private sealed class ConstRng : IBattleRng
    {
        private readonly int _v;

        public ConstRng(int v) => _v = v;

        public int NextValue() => _v;
    }
}
