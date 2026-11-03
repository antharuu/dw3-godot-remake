using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class LegacyBattleReportConsoleTests
{
    [Fact]
    public void PreservesLegacyConsoleAssertions()
    {
        RaiseStatsAppliesGrowthDeterministically();
        RaiseStatsCapsMaxHp();
        RaiseStatsCapsBattleStatAndResist();
        AddExpLevelsUpAndRaisesStats();
        AddExpStaysPutBelowThreshold();
        AddExpLevelsMultipleLevels();
        AddExpCapsExpAt999999();
        ExpShareOneTwoThreeFought();
        GetDigimonExpBranches();
        AddDigimonExpBranches();
        FoughtMarkedWhenPartnerActs();
        ReportNeverEndsWithoutCross();
        ReportShowsExpThenMoneyThenFadesOut();
        ReportLevelUpShowsMessageSeven();
        ReportRollsExpDigitByDigit();
        ReportSkipsUnwiredDigimonSlotsOnce();
    }

    // ---- growth.c: STFGTREP_raiseStats ----
    private static void RaiseStatsAppliesGrowthDeterministically()
    {
        var tables = Tables();
        var state = new PartnerGrowthState
        {
            Level = 10,
            MaxHp = 100,
            MaxMp = 50,
            BattleStats = [10, 10, 10, 10, 10, 10],
            Resistances = [5, 5, 5, 5, 5, 5, 5],
        };

        // identity tables + a zero RNG make each gain exactly the growth value.
        GrowthCalculator.RaiseStats(tables, state, new PartnerGrowthCurve(0, 10, 5, [2, 3, 4, 2, 3, 4], [2, 2, 2, 2, 2, 2, 2]), level: 10, new NoRng());

        Check(state.MaxHp == 110, "raiseStats added the HP growth (vital cut and swing are zero)");
        Check(state.MaxMp == 55, "raiseStats added the MP growth");
        Check(state.BattleStats.SequenceEqual([12, 13, 14, 12, 13, 14]), "each battle stat grew by its statGrowth column");
        Check(state.Resistances.SequenceEqual([7, 7, 7, 7, 7, 7, 7]), "each resistance grew by its resistGrowth column");
    }

    private static void RaiseStatsCapsMaxHp()
    {
        var tables = Tables();
        var state = new PartnerGrowthState { Level = 10, MaxHp = 9995, MaxMp = 50, BattleStats = new int[6], Resistances = new int[7] };
        GrowthCalculator.RaiseStats(tables, state, new PartnerGrowthCurve(0, 10, 0, [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0, 0]), level: 10, new NoRng());
        Check(state.MaxHp == 9999, "max HP caps at 9999 (growth.c STAT_MAX_HP cap)");
    }

    private static void RaiseStatsCapsBattleStatAndResist()
    {
        var tables = Tables();
        var state = new PartnerGrowthState
        {
            Level = 10,
            MaxHp = 0,
            MaxMp = 0,
            BattleStats = [997, 0, 0, 0, 0, 0],
            Resistances = [997, 0, 0, 0, 0, 0, 0],
        };
        GrowthCalculator.RaiseStats(tables, state, new PartnerGrowthCurve(0, 0, 0, [3, 0, 0, 0, 0, 0], [3, 0, 0, 0, 0, 0, 0]), level: 10, new NoRng());
        Check(state.BattleStats[0] == 999, "a battle stat caps at 999");
        Check(state.Resistances[0] == 999, "a resistance caps at 999");
    }

    // ---- growth.c: STFGTREP_addExp ----
    private static void AddExpLevelsUpAndRaisesStats()
    {
        var tables = Tables();
        var state = new PartnerGrowthState
        {
            Level = 1,
            MaxHp = 100,
            MaxMp = 50,
            BattleStats = [10, 10, 10, 10, 10, 10],
            Resistances = [5, 5, 5, 5, 5, 5, 5],
        };

        // Level 2's threshold: (2^3 + 2*5 - 6) * 10 / 10 + 0 = 12, so exp 13 crosses it.
        var leveled = GrowthCalculator.AddExp(
            tables,
            state,
            new PartnerGrowthCurve(10, 10, 5, [2, 3, 4, 2, 3, 4], [2, 2, 2, 2, 2, 2, 2]),
            exp: 13,
            new NoRng());

        Check(leveled, "adding exp past the threshold levels the partner up");
        Check(state.Level == 2, "the partner reached level 2");
        Check(state.Exp == 13, "the exp was added");
        Check(state.Tp == 5, "each level-up adds 5 TP");
        Check(state.MaxHp == 110, "the level-up raised the max HP via RaiseStats");
        Check(state.BattleStats.SequenceEqual([12, 13, 14, 12, 13, 14]), "the level-up raised the battle stats");
    }

    private static void AddExpStaysPutBelowThreshold()
    {
        var tables = Tables();
        var state = new PartnerGrowthState { Level = 1, MaxHp = 100, MaxMp = 50, BattleStats = new int[6], Resistances = new int[7] };
        var leveled = GrowthCalculator.AddExp(
            tables,
            state,
            new PartnerGrowthCurve(10, 10, 5, [2, 2, 2, 2, 2, 2], [2, 2, 2, 2, 2, 2, 2]),
            exp: 12,
            new NoRng());

        Check(!leveled, "exp below the threshold does not level up");
        Check(state.Level == 1, "the level stays 1");
        Check(state.Exp == 12, "the exp was still added");
    }

    private static void AddExpLevelsMultipleLevels()
    {
        var tables = Tables();
        var state = new PartnerGrowthState { Level = 1, MaxHp = 0, MaxMp = 0, BattleStats = new int[6], Resistances = new int[7] };

        // Thresholds: level 2 = 12, level 3 = 36, level 4 = 78, level 5 = 144 (tier 0, expRate 10).
        var leveled = GrowthCalculator.AddExp(
            tables,
            state,
            new PartnerGrowthCurve(10, 0, 0, [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0, 0]),
            exp: 100,
            new NoRng());

        Check(leveled, "enough exp levels up several times");
        Check(state.Level == 4, "exp 100 reaches level 4 (144 is out of reach)");
        Check(state.Tp == 15, "three level-ups add 15 TP");
    }

    private static void AddExpCapsExpAt999999()
    {
        var tables = Tables();
        var state = new PartnerGrowthState { Level = 99, Exp = 999990, MaxHp = 0, MaxMp = 0, BattleStats = new int[6], Resistances = new int[7] };
        GrowthCalculator.AddExp(
            tables,
            state,
            new PartnerGrowthCurve(10, 0, 0, [0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 0, 0, 0]),
            exp: 100,
            new NoRng());
        Check(state.Exp == 999999, "exp caps at 999999");
    }

    // ---- report.c: exp sharing ----
    private static void ExpShareOneTwoThreeFought()
    {
        Check(GrowthCalculator.ExpShare(4, 1) == 4, "one fighter keeps the whole reward exp");
        Check(GrowthCalculator.ExpShare(4, 2) == 2, "two fighters share six-tenths each (4 * 6 / 10)");
        Check(GrowthCalculator.ExpShare(4, 3) == 1, "three fighters share a third each (4 / 3)");
    }

    // ---- growth.c: digimon exp ----
    private static void GetDigimonExpBranches()
    {
        Check(
            GrowthCalculator.GetDigimonExp(level: 10, digimonExpLevel: 20, entryLevel: 5, exp: 1, used: 1) == 1,
            "digimon exp: one used Digimon gets the whole share");
        Check(
            GrowthCalculator.GetDigimonExp(level: 10, digimonExpLevel: 20, entryLevel: 5, exp: 10, used: 2) == 6,
            "digimon exp: two used Digimon split six-tenths each");
        Check(
            GrowthCalculator.GetDigimonExp(level: 10, digimonExpLevel: 20, entryLevel: 5, exp: 10, used: 3) == 3,
            "digimon exp: three used Digimon split a third each");
        Check(
            GrowthCalculator.GetDigimonExp(level: 10, digimonExpLevel: 20, entryLevel: 5, exp: 100, used: 1) == 10,
            "digimon exp caps at 10 below the Digimon's expLevel");
        Check(
            GrowthCalculator.GetDigimonExp(level: 10, digimonExpLevel: 20, entryLevel: 30, exp: 100, used: 1) == 50,
            "digimon exp caps at 50 past the Digimon's expLevel");
        Check(
            GrowthCalculator.GetDigimonExp(level: 100, digimonExpLevel: 20, entryLevel: 5, exp: 1, used: 1) == 1,
            "a zero share floors at 1");
    }

    private static void AddDigimonExpBranches()
    {
        var level = 1;
        var exp = 0;
        Check(GrowthCalculator.AddDigimonExp(expLevel: 20, ref level, ref exp, 12), "12 exp levels a level-1 Digimon up");
        Check(level == 2, "the Digimon reached level 2 (10 a level below expLevel)");

        level = 1;
        exp = 0;
        Check(!GrowthCalculator.AddDigimonExp(expLevel: 20, ref level, ref exp, 5), "5 exp is below the 10-per-level need");
        Check(level == 1, "the Digimon stayed level 1");

        level = 21;
        exp = 0;
        Check(!GrowthCalculator.AddDigimonExp(expLevel: 20, ref level, ref exp, 60), "past expLevel the need jumps to 50 a level");
        Check(level == 21, "60 exp is below the 290 need at level 21");

        level = 98;
        exp = 0;
        Check(GrowthCalculator.AddDigimonExp(expLevel: 20, ref level, ref exp, 100000), "a huge dump levels up");
        Check(level == 99, "the Digimon caps at 99");
    }

    // ---- battle integration: BATTLE_RESULT.partners[i].fought ----
    private static void FoughtMarkedWhenPartnerActs()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 500, MaxHp = 500, Mp = 100, MaxMp = 100, Stats = Stats(attack: 50) };
        setup.Enemies[0] = new BattleEnemy { Fighter = 101, Hp = 500, Mp = 0, Strength = 16, BaseStats = Stats(defense: 10) };
        var state = BattleState.InitFighters(setup, digimon: 1);

        Check(!state.Fought.Any(f => f), "no partner fought before the first turn");
        state.PlayerAttack(new TechData { Power = 20, Mp = 0 }, new NoRng());
        Check(state.Fought[0], "the active partner is marked fought after it attacks");
        Check(!state.Fought[1] && !state.Fought[2], "the bench partners are not marked fought");
    }

    // ---- fixtures ----
    private static GrowthTables Tables()
    {
        var tables = new GrowthTables();
        tables.LevelExp = [0, 0, 0, 0];
        tables.VitalCuts = [0, 0, 0, 0];
        tables.VitalRandom = [0, 0, 0, 0, 0, 0, 0, 0, 0];
        for (var tier = 0; tier < 6; tier++)
        {
            for (var col = 0; col < 9; col++)
            {
                tables.StatGains[tier][col] = col;   // identity: gain == growth column
            }
        }

        tables.ResistGains = [0, 1, 2, 3, 4, 5, 6, 7];
        return tables;
    }

    private static BattleStats Stats(int attack = 0, int defense = 0, int spirit = 0, int wisdom = 0, int speed = 0)
    {
        var s = new BattleStats();
        s.Stats[BattleStat.Attack] = attack;
        s.Stats[BattleStat.Defense] = defense;
        s.Stats[BattleStat.Spirit] = spirit;
        s.Stats[BattleStat.Wisdom] = wisdom;
        s.Stats[BattleStat.Speed] = speed;
        return s;
    }

    private sealed class NoRng : IBattleRng
    {
        public int NextValue() => 0;
    }

    // ---- report.c / partner.c: engine-free STFGTREP controller ----
    private static void ReportNeverEndsWithoutCross()
    {
        var report = new FightReportController(SampleInput());
        for (var i = 0; i < 10_000; i++)
        {
            report.Step(crossPressedEdge: false);
        }

        Check(
            !report.View.Done && report.View.Message is { Entry: 2 },
            "report waits on its first EXP message without Cross");
        Check(report.View.Message?.ArrowVisible == true, "the completed EXP page shows its Cross arrow");
    }

    private static void ReportShowsExpThenMoneyThenFadesOut()
    {
        var report = new FightReportController(SampleInput());
        var seen = new List<int>();
        for (var i = 0; i < 20_000 && !report.View.Done; i++)
        {
            var entry = report.View.Message?.Entry ?? -1;
            if (entry >= 0 && (seen.Count == 0 || seen[^1] != entry))
            {
                seen.Add(entry);
            }

            report.Step(crossPressedEdge: i % 20 == 0);
        }

        Check(report.View.Done, "report reaches Done with Cross presses");
        Check(
            seen.SequenceEqual([2, 10]),
            $"FGTRPT messages={string.Join(",", seen)} expected=2,10");
        Check(report.View.ExitFade == 0x1000, "the 30-frame report fader reaches opaque black at Done");
    }

    private static void ReportLevelUpShowsMessageSeven()
    {
        var partner = SampleInput().Partners[0] with { LevelAfter = 26 };
        var report = new FightReportController(SampleInput() with { Partners = [partner] });
        var sawSeven = false;
        for (var i = 0; i < 20_000 && !report.View.Done; i++)
        {
            sawSeven |= report.View.Message?.Entry == 7;
            report.Step(crossPressedEdge: i % 20 == 0);
        }

        Check(sawSeven, "partner level up shows FGTRPT 7");
    }

    private static void ReportRollsExpDigitByDigit()
    {
        Check(
            FightReportController.RollStep(4) == 1 && FightReportController.RollStep(250) == 111,
            "rollExp step is 1 per digit (4 -> 1, 250 -> 111)");
        var report = new FightReportController(SampleInput());
        var shown = new List<int>();
        for (var i = 0; i < 300; i++)
        {
            shown.Add(report.View.Partners[0].ShownExp);
            report.Step(i % 20 == 0);
        }

        Check(
            shown.Contains(1) && shown.Contains(2) && shown.Contains(3) && shown.Contains(4),
            "rollExp exposes the source counter's intermediate digits before target EXP");
    }

    private static void ReportSkipsUnwiredDigimonSlotsOnce()
    {
        var report = new FightReportController(SampleInput());
        for (var i = 0; i < 20_000 && !report.View.Done; i++)
        {
            report.Step(i % 20 == 0);
        }

        Check(
            report.Logs.Count(line => line == "report-digimon-exp-unwired") == 1,
            "unwired Digimon-slot EXP is logged once, not simulated");
        Check(
            report.View.Partners.Single().Slots.All(slot => !slot.ExpApplied),
            "unwired Digimon slots never receive invented EXP");
    }

    private static FightReportInput SampleInput() => new(
        [new FightReportPartner(0, 0, 383, 25, 0, 4, 25, [])], Money: 50, Item: 0);

    private static void Check(bool condition, string name)
    {
        Assert.True(condition, name);
    }
}
