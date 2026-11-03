using System.Text.Json.Nodes;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Differential regression suite for DamageCalculator.
/// Reads 300+ fabricated test vectors from Vectors/damage-calculator.json,
/// replays each against the current implementation, and asserts the output
/// matches the recorded expected value.
///
/// The vectors were generated from the current implementation with seed 42
/// and cover boundary values, edge cases, and random inputs for every public
/// method.  No game values are embedded: all inputs are invented.
/// </summary>
public sealed class DamageCalculatorVectorTests
{
    private static readonly string VectorsPath = Path.Combine(
        AppContext.BaseDirectory, "Vectors", "damage-calculator.json");

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
                    case "GetElementBoost":
                        VerifyGetElementBoost(c, id, errors);
                        break;

                    case "GetTurnDelay":
                        VerifyGetTurnDelay(c, id, errors);
                        break;

                    case "TestRunAway":
                        VerifyTestRunAway(c, id, errors);
                        break;

                    case "TestEnemyRunAway":
                        VerifyTestEnemyRunAway(c, id, errors);
                        break;

                    case "ComputeDamage":
                        VerifyComputeDamage(c, id, errors);
                        break;

                    case "ComputeMagicDamage":
                        VerifyComputeMagicDamage(c, id, errors);
                        break;

                    case "ComputeCounterDamage":
                        VerifyComputeCounterDamage(c, id, errors);
                        break;

                    case "ComputeEnemyStats":
                        VerifyComputeEnemyStats(c, id, errors);
                        break;

                    case "RollCritical":
                        VerifyRollCritical(c, id, errors);
                        break;

                    case "RollMagicCritical":
                        VerifyRollMagicCritical(c, id, errors);
                        break;

                    default:
                        errors.Add($"[id={id}] Unknown function '{fn}'");
                        break;
                }
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                errors.Add($"[id={id}] Exception in '{fn}': {ex.Message}");
            }
#pragma warning restore CA1031
        }

        Assert.True(
            errors.Count == 0,
            $"{errors.Count} vector(s) failed:\n" + string.Join("\n", errors));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Per-function verifiers
    // ─────────────────────────────────────────────────────────────────────
    private static void VerifyGetElementBoost(JsonNode c, int id, List<string> errors)
    {
        int value = I(c, "value");
        int element = I(c, "element");
        int boostElem = I(c, "boostElement");
        int boostAmount = I(c, "boostAmount");
        int expected = I(c, "expected");

        int actual = DamageCalculator.GetElementBoost(value, element, new ElementBoost(boostElem, boostAmount));
        if (actual != expected)
        {
            errors.Add($"[id={id}] GetElementBoost(value={value},element={element}," +
                       $"boostElement={boostElem},boostAmount={boostAmount}) = {actual}, expected {expected}");
        }
    }

    private static void VerifyGetTurnDelay(JsonNode c, int id, List<string> errors)
    {
        int ownSpeed = I(c, "ownSpeed");
        int otherSpeed = I(c, "otherSpeed");
        int expected = I(c, "expected");

        int actual = DamageCalculator.GetTurnDelay(ownSpeed, otherSpeed);
        if (actual != expected)
        {
            errors.Add($"[id={id}] GetTurnDelay(own={ownSpeed},other={otherSpeed}) = {actual}, expected {expected}");
        }
    }

    private static void VerifyTestRunAway(JsonNode c, int id, List<string> errors)
    {
        int runAttempts = I(c, "runAttempts");
        int atkLevel = I(c, "atkLevel");
        int defLevel = I(c, "defLevel");
        int atkSpeed = I(c, "atkSpeed");
        int defSpeed = I(c, "defSpeed");
        int bonus = I(c, "runAwayBonus");
        bool block = B(c, "blockRunAway");
        int atkFlags = I(c, "atkFlags");
        int rngValue = I(c, "rngValue");
        bool expected = B(c, "expected");

        var atk = new BattleStats { Level = atkLevel, RunAwayBonus = bonus };
        atk.Stats[BattleStat.Speed] = atkSpeed;
        var def = new BattleStats { Level = defLevel };
        def.Stats[BattleStat.Speed] = defSpeed;

        bool actual = RunAwayRules.TestRunAway(runAttempts, atk, def, block, atkFlags, rngValue);
        if (actual != expected)
        {
            errors.Add($"[id={id}] TestRunAway(attempts={runAttempts},atkLvl={atkLevel}," +
                       $"defLvl={defLevel},atkSpd={atkSpeed},defSpd={defSpeed}," +
                       $"bonus={bonus},block={block},flags=0x{atkFlags:X},rng={rngValue}) = {actual}, expected {expected}");
        }
    }

    private static void VerifyTestEnemyRunAway(JsonNode c, int id, List<string> errors)
    {
        int atkSpeed = I(c, "atkSpeed");
        int defSpeed = I(c, "defSpeed");
        int runAwayGuard = I(c, "runAwayGuard");
        int atkFlags = I(c, "atkFlags");
        int rngValue = I(c, "rngValue");
        bool expected = B(c, "expected");

        var atk = new BattleStats();
        atk.Stats[BattleStat.Speed] = atkSpeed;
        var def = new BattleStats { RunAwayGuard = runAwayGuard };
        def.Stats[BattleStat.Speed] = defSpeed;

        bool actual = RunAwayRules.TestEnemyRunAway(atk, def, atkFlags, rngValue);
        if (actual != expected)
        {
            errors.Add($"[id={id}] TestEnemyRunAway(atkSpd={atkSpeed},defSpd={defSpeed}," +
                       $"guard={runAwayGuard},flags=0x{atkFlags:X},rng={rngValue}) = {actual}, expected {expected}");
        }
    }

    private static void VerifyComputeDamage(JsonNode c, int id, List<string> errors)
    {
        var tech = ReadTech(c);
        var user = ReadUserStats(c);
        var target = ReadTargetStats(c);
        bool enemy = B(c, "enemyAttacker");
        int str = I(c, "strength");
        int bElem = I(c, "boostElement");
        int bAmt = I(c, "boostAmount");
        int crit = I(c, "critRoll");
        int exp = I(c, "expected");

        int actual = DamageCalculator.ComputeDamage(tech, user, target, enemy, str, new ElementBoost(bElem, bAmt), crit);
        if (actual != exp)
        {
            errors.Add($"[id={id}] ComputeDamage(power={tech.Power},atkAtk={user.Stats[BattleStat.Attack]}," +
                       $"defDef={target.Stats[BattleStat.Defense]},enemy={enemy},str={str},crit={crit}) = {actual}, expected {exp}");
        }
    }

    private static void VerifyComputeMagicDamage(JsonNode c, int id, List<string> errors)
    {
        var tech = new TechData
        {
            Power = I(c, "techPower"),
            Element = I(c, "techElement"),
            ElementPower = I(c, "techElementPower"),
            Family = I(c, "techFamily"),
            Effect = I(c, "techEffect"),
            EffectPower = I(c, "techEffectPower"),
        };
        var user = new BattleStats();
        user.Stats[BattleStat.Spirit] = I(c, "userSpirit");

        var target = new BattleStats { Family = I(c, "targetFamily"), DamageCut = I(c, "targetDamageCut") };
        target.Stats[BattleStat.Spirit] = I(c, "targetSpirit");
        target.Resist[0] = I(c, "targetResist0");
        target.Resist[1] = I(c, "targetResist1");
        target.Resist[2] = I(c, "targetResist2");
        target.Resist[3] = I(c, "targetResist3");
        target.Resist[4] = I(c, "targetResist4");
        target.Resist[5] = I(c, "targetResist5");
        target.Resist[6] = I(c, "targetResist6");

        bool enemy = B(c, "enemyAttacker");
        int str = I(c, "strength");
        int bElem = I(c, "boostElement");
        int bAmt = I(c, "boostAmount");
        int crit = I(c, "critRoll");
        int exp = I(c, "expected");

        int actual = DamageCalculator.ComputeMagicDamage(tech, user, target, enemy, str, new ElementBoost(bElem, bAmt), crit);
        if (actual != exp)
        {
            errors.Add($"[id={id}] ComputeMagicDamage(power={tech.Power},elem={tech.Element}," +
                       $"userSpirit={user.Stats[BattleStat.Spirit]},targetSpirit={target.Stats[BattleStat.Spirit]}," +
                       $"enemy={enemy},str={str},crit={crit}) = {actual}, expected {exp}");
        }
    }

    private static void VerifyComputeCounterDamage(JsonNode c, int id, List<string> errors)
    {
        var tech = new TechData
        {
            Element = I(c, "techElement"),
            ElementPower = I(c, "techElementPower"),
            Family = I(c, "techFamily"),
            Effect = I(c, "techEffect"),
            EffectPower = I(c, "techEffectPower"),
            Script = I(c, "techScript"),
        };
        var user = new BattleStats
        {
            Element = I(c, "userElement"),
            ElementPower = I(c, "userElementPower"),
            Family = I(c, "userFamily"),
            DamageBonus = I(c, "userDamageBonus"),
            TripleHit = I(c, "userTripleHit"),
        };
        user.Stats[BattleStat.Attack] = I(c, "userAttack");
        user.WeaponFamilies[0] = I(c, "userWeaponFamily0");
        user.WeaponFamilies[1] = I(c, "userWeaponFamily1");
        user.WeaponFamilies[2] = I(c, "userWeaponFamily2");

        var target = new BattleStats { Family = I(c, "targetFamily"), DamageCut = I(c, "targetDamageCut") };
        target.Stats[BattleStat.Defense] = I(c, "targetDefense");
        target.Resist[0] = I(c, "targetResist0");
        target.Resist[1] = I(c, "targetResist1");
        target.Resist[2] = I(c, "targetResist2");
        target.Resist[3] = I(c, "targetResist3");
        target.Resist[4] = I(c, "targetResist4");
        target.Resist[5] = I(c, "targetResist5");
        target.Resist[6] = I(c, "targetResist6");

        int recv = I(c, "receivedDamage");
        int bElem = I(c, "boostElement");
        int bAmt = I(c, "boostAmount");
        int crit = I(c, "critRoll");
        bool special = B(c, "special");
        int exp = I(c, "expected");

        int actual = DamageCalculator.ComputeCounterDamage(tech, user, target, recv, new ElementBoost(bElem, bAmt), crit, special);
        if (actual != exp)
        {
            errors.Add($"[id={id}] ComputeCounterDamage(recv={recv},effectPower={tech.EffectPower},special={special}) = {actual}, expected {exp}");
        }
    }

    private static void VerifyComputeEnemyStats(JsonNode c, int id, List<string> errors)
    {
        var entry = new BattleStats { Family = I(c, "entryFamily") };
        entry.Stats[0] = I(c, "entryAttack");
        entry.Stats[1] = I(c, "entryDefense");
        entry.Stats[2] = I(c, "entrySpirit");
        entry.Stats[3] = I(c, "entryWisdom");
        entry.Stats[4] = I(c, "entrySpeed");
        entry.Resist[0] = I(c, "entryResist0");
        entry.Resist[1] = I(c, "entryResist1");
        entry.Resist[2] = I(c, "entryResist2");
        entry.Resist[3] = I(c, "entryResist3");
        entry.Resist[4] = I(c, "entryResist4");
        entry.Resist[5] = I(c, "entryResist5");
        entry.Resist[6] = I(c, "entryResist6");

        int level = I(c, "level");
        int strength = I(c, "strength");

        int expAtk = I(c, "expectedAttack");
        int expDef = I(c, "expectedDefense");
        int expSpr = I(c, "expectedSpirit");
        int expWis = I(c, "expectedWisdom");
        int expSpd = I(c, "expectedSpeed");
        int expFam = I(c, "expected");

        var result = DamageCalculator.ComputeEnemyStats(entry, level, strength);

        if (result.Level != level)
        {
            errors.Add($"[id={id}] ComputeEnemyStats: Level={result.Level}, expected {level}");
        }

        if (result.Family != expFam)
        {
            errors.Add($"[id={id}] ComputeEnemyStats: Family={result.Family}, expected {expFam}");
        }

        if (result.Stats[0] != expAtk)
        {
            errors.Add($"[id={id}] ComputeEnemyStats: Attack={result.Stats[0]}, expected {expAtk}");
        }

        if (result.Stats[1] != expDef)
        {
            errors.Add($"[id={id}] ComputeEnemyStats: Defense={result.Stats[1]}, expected {expDef}");
        }

        if (result.Stats[2] != expSpr)
        {
            errors.Add($"[id={id}] ComputeEnemyStats: Spirit={result.Stats[2]}, expected {expSpr}");
        }

        if (result.Stats[3] != expWis)
        {
            errors.Add($"[id={id}] ComputeEnemyStats: Wisdom={result.Stats[3]}, expected {expWis}");
        }

        if (result.Stats[4] != expSpd)
        {
            errors.Add($"[id={id}] ComputeEnemyStats: Speed={result.Stats[4]}, expected {expSpd}");
        }

        // Resist is copied verbatim; verify a spot check (resist[0])
        if (result.Resist[0] != I(c, "entryResist0"))
        {
            errors.Add($"[id={id}] ComputeEnemyStats: Resist[0]={result.Resist[0]}, expected {I(c, "entryResist0")}");
        }
    }

    private static void VerifyRollCritical(JsonNode c, int id, List<string> errors)
    {
        var tech = new TechData
        {
            Element = I(c, "techElement"),
            ElementPower = I(c, "techElementPower"),
            Family = I(c, "techFamily"),
            Effect = I(c, "techEffect"),
            EffectPower = I(c, "techEffectPower"),
        };
        var atk = new BattleStats { Family = I(c, "atkFamily"), CriticalBonus = I(c, "atkCriticalBonus") };
        atk.WeaponFamilies[0] = I(c, "atkWeaponFamily0");
        atk.WeaponFamilies[1] = I(c, "atkWeaponFamily1");
        atk.WeaponFamilies[2] = I(c, "atkWeaponFamily2");
        var def = new BattleStats { Family = I(c, "defFamily") };

        var atkF = new BattleFighter { Special = B(c, "atkFighterSpecial") };
        var defF = new BattleFighter
        {
            Flags = I(c, "defFighterFlags"),
            Paralysis = I(c, "defFighterParalysis"),
            Sleep = I(c, "defFighterSleep"),
            Confusion = I(c, "defFighterConfusion"),
        };

        int rng = I(c, "rngValue");
        bool expected = B(c, "expected");

        bool actual = CriticalHitRules.RollCritical(tech, atk, def, atkF, defF, rng);
        if (actual != expected)
        {
            errors.Add($"[id={id}] RollCritical(techFam={tech.Family},techEffect={tech.Effect}," +
                       $"atkCritBonus={atk.CriticalBonus},defFam={def.Family},rng={rng}) = {actual}, expected {expected}");
        }
    }

    private static void VerifyRollMagicCritical(JsonNode c, int id, List<string> errors)
    {
        var tech = new TechData
        {
            Element = I(c, "techElement"),
            ElementPower = I(c, "techElementPower"),
            Family = I(c, "techFamily"),
        };
        var def = new BattleStats { Family = I(c, "defFamily") };
        def.Resist[0] = I(c, "defResist0");
        def.Resist[1] = I(c, "defResist1");
        def.Resist[2] = I(c, "defResist2");
        def.Resist[3] = I(c, "defResist3");
        def.Resist[4] = I(c, "defResist4");
        def.Resist[5] = I(c, "defResist5");
        def.Resist[6] = I(c, "defResist6");

        var defF = new BattleFighter
        {
            Flags = I(c, "defFighterFlags"),
            Paralysis = I(c, "defFighterParalysis"),
            Sleep = I(c, "defFighterSleep"),
            Confusion = I(c, "defFighterConfusion"),
        };

        int rng = I(c, "rngValue");
        bool expected = B(c, "expected");

        bool actual = CriticalHitRules.RollMagicCritical(tech, def, defF, rng);
        if (actual != expected)
        {
            errors.Add($"[id={id}] RollMagicCritical(elem={tech.Element},elemPow={tech.ElementPower}," +
                       $"resist={def.Resist[tech.Element - BattleConst.ElementFirst]},rng={rng}) = {actual}, expected {expected}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers to build BattleStats from JSON field prefixes
    // ─────────────────────────────────────────────────────────────────────
    private static TechData ReadTech(JsonNode c) => new()
    {
        Power = I(c, "techPower"),
        Element = I(c, "techElement"),
        ElementPower = I(c, "techElementPower"),
        Family = I(c, "techFamily"),
        Effect = I(c, "techEffect"),
        EffectPower = I(c, "techEffectPower"),
        Script = I(c, "techScript"),
    };

    private static BattleStats ReadUserStats(JsonNode c)
    {
        var s = new BattleStats
        {
            Family = I(c, "userFamily"),
            Element = I(c, "userElement"),
            ElementPower = I(c, "userElementPower"),
            DamageBonus = I(c, "userDamageBonus"),
            TripleHit = I(c, "userTripleHit"),
            CriticalBonus = I(c, "userCriticalBonus"),
            RunAwayBonus = I(c, "userRunAwayBonus"),
            RunAwayGuard = I(c, "userRunAwayGuard"),
        };
        s.Stats[BattleStat.Attack] = I(c, "userAttack");
        s.Stats[BattleStat.Defense] = I(c, "userDefense");
        s.Stats[BattleStat.Spirit] = I(c, "userSpirit");
        s.Stats[BattleStat.Wisdom] = I(c, "userWisdom");
        s.Stats[BattleStat.Speed] = I(c, "userSpeed");
        s.WeaponFamilies[0] = I(c, "userWeaponFamily0");
        s.WeaponFamilies[1] = I(c, "userWeaponFamily1");
        s.WeaponFamilies[2] = I(c, "userWeaponFamily2");
        return s;
    }

    private static BattleStats ReadTargetStats(JsonNode c)
    {
        var t = new BattleStats { Family = I(c, "targetFamily"), DamageCut = I(c, "targetDamageCut") };
        t.Stats[BattleStat.Attack] = I(c, "targetAttack");
        t.Stats[BattleStat.Defense] = I(c, "targetDefense");
        t.Stats[BattleStat.Spirit] = I(c, "targetSpirit");
        t.Resist[0] = I(c, "targetResist0");
        t.Resist[1] = I(c, "targetResist1");
        t.Resist[2] = I(c, "targetResist2");
        t.Resist[3] = I(c, "targetResist3");
        t.Resist[4] = I(c, "targetResist4");
        t.Resist[5] = I(c, "targetResist5");
        t.Resist[6] = I(c, "targetResist6");
        return t;
    }

    // ─────────────────────────────────────────────────────────────────────
    // JSON helpers
    // ─────────────────────────────────────────────────────────────────────
    private static int I(JsonNode c, string k) => c[k]!.GetValue<int>();

    private static bool B(JsonNode c, string k) => c[k]!.GetValue<bool>();
}
