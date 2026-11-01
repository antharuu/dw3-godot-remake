using Xunit;

namespace Dw3.Game.Tests;

using Dw3Production.Simulation;

public sealed class LegacyEncounterConsoleTests
{

    [Fact]
    public void PreservesLegacyConsoleAssertions()
    {
        RollBoundsExhaustiveNoGap();
        RollBranchBoundaries();
        DecrementsByAreaRate();
        TriggersWhenStepsRunOut();
        DrawsAreaBattleWithIndexMask();
        EventBattleUsesFourthArea();
        GuardRandomBattlesOffStillRerolls();
        GuardBusySkipsCounting();
        GuardNoAreaCellSkipsCounting();
        GuardNoBattlesTableSkipsCounting();
    }

    // Every RANDOM.next() % 2304 value (0..2303) rolls a countdown in 0..1279, and the
    // two branches together cover every value in 0..1279 exactly once — branch 1 (value
    // < 0x100) yields 0..255, branch 2 yields 256..1279. No gap, no value outside range.
    private static void RollBoundsExhaustiveNoGap()
    {
        var seen = new HashSet<int>();
        for (var value = 0; value < 2304; value++)
        {
            var controller = new EncounterController(new ScriptedRng(value));
            controller.RollBattleSteps();
            Check(controller.BattleSteps is >= 0 and <= 1279, $"roll value=0x{value:X3} yields in-range steps");
            seen.Add(controller.BattleSteps);
        }
        for (var steps = 0; steps <= 1279; steps++)
            Check(seen.Contains(steps), $"steps value {steps} is reachable by the two branches (no gap)");
        Check(seen.Count == 1280, "roll covers exactly the 1280 distinct step values");
    }

    // The branch boundary: 0x100 (256) is the first value of branch 2, so its result is
    // 256, not 128 — the countdown never lands below 256 on the high branch.
    private static void RollBranchBoundaries()
    {
        Check(StepsFor(0x000) == 0, "value 0 rolls 0 (branch 1 floor)");
        Check(StepsFor(0x0FF) == 255, "value 0xFF rolls 255 (branch 1 ceiling)");
        Check(StepsFor(0x100) == 256, "value 0x100 rolls 256 (branch 2 floor, not 128)");
        Check(StepsFor(0x8FF) == 1279, "value 0x8FF (2303) rolls 1279 (branch 2 ceiling)");
    }

    private static int StepsFor(int value)
    {
        var controller = new EncounterController(new ScriptedRng(value));
        controller.RollBattleSteps();
        return controller.BattleSteps;
    }

    // Each step lowers the countdown by FIELDSTG_battleRates[area.count], which is read
    // from the area's BattleList.Count, not a fixed constant.
    private static void DecrementsByAreaRate()
    {
        var count1 = new EncounterController(new ScriptedRng(0)) { RandomBattles = true, BattleSteps = 10 };
        count1.SetBattles(FourAreas(100, count: 1));
        var record = count1.CountBattleSteps(1);
        Check(record is null, "one step above zero starts nothing");
        Check(count1.BattleSteps == 7, "steps decrement by battleRates[1] = 3");

        var count5 = new EncounterController(new ScriptedRng(0)) { RandomBattles = true, BattleSteps = 100 };
        count5.SetBattles(FourAreas(100, count: 5));
        count5.CountBattleSteps(1);
        Check(count5.BattleSteps == 82, "steps decrement by battleRates[5] = 18");
    }

    private static void TriggersWhenStepsRunOut()
    {
        var controller = new EncounterController(new ScriptedRng(0x123)) { RandomBattles = true, BattleSteps = 3 };
        controller.SetBattles(FourAreas(100, count: 1));
        var record = controller.CountBattleSteps(1);
        Check(record is not null, "steps reaching zero start an area battle");
        Check(record!.Kind == "area" && record.Area == 0 && record.Index == 3, "area battle draws index = rng & 7 (0x123 & 7 = 3)");
        Check(record.Battle.Encounter == 103, "area battle is battles[0].battles[3]");
        Check(controller.BattleSteps == 273, "steps are re-rolled after the trigger (0x123 -> 273)");
    }

    private static void DrawsAreaBattleWithIndexMask()
    {
        var low = new EncounterController(new ScriptedRng(0x100));
        low.SetBattles(FourAreas(200, count: 2));
        var lowRecord = low.StartAreaBattle(2);
        Check(lowRecord.Index == 0 && lowRecord.Area == 2, "area battle index = 0x100 & 7 = 0");
        Check(lowRecord.Battle.Encounter == 216, "area 2 base 216 selects battle 0");

        var high = new EncounterController(new ScriptedRng(0x107));
        high.SetBattles(FourAreas(200, count: 2));
        var highRecord = high.StartAreaBattle(0);
        Check(highRecord.Index == 7 && highRecord.Battle.Encounter == 207, "index mask & 7 caps at 7 (0x107 & 7 = 7)");
    }

    private static void EventBattleUsesFourthArea()
    {
        var controller = new EncounterController(new ScriptedRng(0));
        controller.SetBattles(FourAreas(300, count: 0));
        var record = controller.StartEventBattle(5);
        Check(record is not null, "event battle resolves from a loaded table");
        Check(record!.Kind == "event" && record.Area == 3 && record.Index == 5, "event battle draws from the fourth area (3)");
        Check(record.Battle.Encounter == 329, "event battle is battles[3].battles[5]");
    }

    private static void GuardRandomBattlesOffStillRerolls()
    {
        var controller = new EncounterController(new ScriptedRng(0x7FF)) { RandomBattles = false, BattleSteps = 3 };
        controller.SetBattles(FourAreas(400, count: 1));
        var record = controller.CountBattleSteps(1);
        Check(record is null, "randomBattles off starts no battle");
        Check(controller.BattleSteps == 1151, "randomBattles off still re-rolls the steps (0x7FF -> 1151)");
    }

    private static void GuardBusySkipsCounting()
    {
        var controller = new EncounterController(new ScriptedRng(0)) { RandomBattles = true, Busy = true, BattleSteps = 3 };
        controller.SetBattles(FourAreas(500, count: 1));
        var record = controller.CountBattleSteps(1);
        Check(record is null && controller.BattleSteps == 3, "busy field skips counting entirely");
    }

    private static void GuardNoAreaCellSkipsCounting()
    {
        var controller = new EncounterController(new ScriptedRng(0)) { RandomBattles = true, BattleSteps = 3 };
        controller.SetBattles(FourAreas(600, count: 1));
        var record = controller.CountBattleSteps(0);
        Check(record is null && controller.BattleSteps == 3, "area cell 0 (no area) skips counting");
    }

    private static void GuardNoBattlesTableSkipsCounting()
    {
        var controller = new EncounterController(new ScriptedRng(0)) { RandomBattles = true, BattleSteps = 3 };
        var record = controller.CountBattleSteps(1);
        Check(record is null && controller.BattleSteps == 3, "missing battles table skips counting");
        Check(controller.StartEventBattle(0) is null, "event battle without a battles table is skipped");
    }

    private static IReadOnlyList<BattleList> FourAreas(int encounterBase, int count)
    {
        var areas = new BattleList[4];
        for (var area = 0; area < 4; area++)
        {
            var baseEncounter = encounterBase + area * 8;
            var battles = new AreaBattle[8];
            for (var slot = 0; slot < 8; slot++) battles[slot] = new AreaBattle(baseEncounter + slot, 0, 0);
            areas[area] = new BattleList(count, battles);
        }
        return areas;
    }

    private sealed class ScriptedRng : IEncounterRng
    {
        private readonly int[] _values;
        private int _index;

        public ScriptedRng(params int[] values) => _values = values;

        public int Draw() => _values[Math.Min(_index++, _values.Length - 1)];
    }

    private static void Check(bool condition, string name)
    {
        Assert.True(condition, name);
    }
}
