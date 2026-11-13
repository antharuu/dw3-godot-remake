using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

/// <summary>
/// Verifies that a won battle credits the session at the original report's stages: nothing at the battle's end,
/// the partner's experience once its EXP page is confirmed, the money when its page opens and the item when its page
/// opens, each exactly once whatever the input, the report return and the next battle.
/// </summary>
public sealed class ReportSettlementChronologyTests
{
    private static readonly BattleReward Reward = new(DigimonExp: 0, Exp: 40, Money: 50);

    /// <summary>The battle's end snapshots the awards without crediting the session.</summary>
    [Fact]
    public void TerminalTick_SnapshotsTheAwardsWithoutCreditingTheSession()
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int> { [RewardSessionFixture.DropItem] = 2 });
        BattleOutcomeResolution resolution = Resolve(session);

        Assert.Equal((40, 50, RewardSessionFixture.DropItem), (resolution.FightReport.Partners[0].ExpGained, resolution.FightReport.Money, resolution.FightReport.Item));
        Assert.Equal((0, 1, 0), (session.Partners[0]!.Growth!.Exp, session.Partners[0]!.Growth!.Level, session.Money));
        Assert.Equal(2, session.Inventory[RewardSessionFixture.DropItem]);
        Assert.Equal(1, session.Partners[0]!.BattleSeed.Stats.Level);
    }

    /// <summary>Each page sees the session the original shows at that point, and the report ends fully credited once.</summary>
    [Fact]
    public void Report_CreditsExperienceMoneyAndItemAtTheirOriginalStages()
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int> { [RewardSessionFixture.DropItem] = 2 });
        BattleOutcomeResolution resolution = Resolve(session);
        var report = new FightReportController(resolution.FightReport, null, resolution.Settlement);
        var seen = new List<(int Entry, int Exp, int Money, int Stock)>();

        ReadToEnd(report, page => seen.Add((page, session.Partners[0]!.Growth!.Exp, session.Money, session.Inventory[RewardSessionFixture.DropItem])));

        Assert.Equal(2, seen[0].Entry);
        Assert.Equal((0, 0, 2), (seen[0].Exp, seen[0].Money, seen[0].Stock));
        Assert.All(seen.Where(page => page.Entry is not (2 or 10 or 11)), page => Assert.Equal((40, 0, 2), (page.Exp, page.Money, page.Stock)));
        (int _, int moneyExp, int moneyWallet, int moneyStock) = seen.Single(page => page.Entry == 10);
        (int _, int itemExp, int itemWallet, int itemStock) = seen.Single(page => page.Entry == 11);
        Assert.Equal((40, 50, 2), (moneyExp, moneyWallet, moneyStock));
        Assert.Equal((40, 50, 3), (itemExp, itemWallet, itemStock));
        AssertCreditedOnce(session);
    }

    /// <summary>Repeated presses, steps after the end, a late full commit and the next battle never credit again.</summary>
    [Fact]
    public void RepeatedInputReportReturnAndNextBattle_NeverCreditTwice()
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int> { [RewardSessionFixture.DropItem] = 2 });
        BattleOutcomeResolution resolution = Resolve(session);
        var report = new FightReportController(resolution.FightReport, null, resolution.Settlement);

        for (int frame = 0; frame < 20_000 && !report.View.Done; frame++)
        {
            report.Step(crossPressedEdge: true);
        }

        for (int extra = 0; extra < 200; extra++)
        {
            report.Step(crossPressedEdge: true);
        }

        VictorySettlement settlement = Assert.IsType<VictorySettlement>(resolution.Settlement);
        settlement.CommitAll();
        settlement.RaisePartner(resolution.FightReport.Partners[0]);
        settlement.CreditMoney();
        settlement.CreditItem();

        Assert.True(report.View.Done);
        AssertCreditedOnce(session);
        BattleFlowController next = RewardSessionFixture.Build(session);
        Assert.Equal(3, Assert.IsType<BattleFighter>(next.State.Players[0]).Stats.Level);
        Assert.Equal(3, next.ItemStock[RewardSessionFixture.DropItem]);
        AssertCreditedOnce(session);
    }

    /// <summary>The panel shows the level before the raise, then the new level once the partner's experience is given.</summary>
    [Fact]
    public void Panel_ShowsTheLevelBeforeItsRaiseAndTheNewLevelAfter()
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int>());
        BattleOutcomeResolution resolution = Resolve(session);
        var report = new FightReportController(resolution.FightReport, null, resolution.Settlement);
        var levels = new List<(int Entry, int Level)>();

        ReadToEnd(report, page => levels.Add((page, report.View.Partners[0].Level)));

        Assert.Equal((2, 1), levels[0]);
        Assert.Equal(3, levels.Single(page => page.Entry == 10).Level);
    }

    /// <summary>A form learned in the report takes the first free slot row when it is announced, at its new level.</summary>
    [Fact]
    public void LearnedForm_TakesItsFreeSlotRowWhenAnnounced()
    {
        PlayerSession session = FormProgressFixture.Session(level: 25, [5, 6], [5, 6]);
        BattleFlowController flow = FormProgressFixture.Build(session);
        FormProgressFixture.FightAs(flow, [5]);
        DigivolutionEntry[] tree = [new DigivolutionEntry(0, FormProgressFixture.UnlockedForm, [new EvolutionNeed(0, 5, 2)], 0, 0)];
        BattleOutcomeResolution resolution = GameHost.BattleSetupCoordinator.ResolveOutcome(session, flow, () => FormProgressFixture.Victory(30, tree))
            ?? throw new InvalidOperationException("battle was not won");
        var report = new FightReportController(resolution.FightReport, null, resolution.Settlement);
        var rows = new List<(int Entry, int[] Slots, int[] Levels)>();

        Assert.Equal([5, 6], session.Partners[0]!.EquippedForms);
        ReadToEnd(report, page => rows.Add((page, [.. report.View.Partners[0].Slots.Select(slot => slot.DigimonId)], [.. report.View.Partners[0].Slots.Select(slot => slot.Level)])));

        Assert.Equal([5, 6, -1], rows[0].Slots);
        Assert.Equal([1, 1, 0], rows[0].Levels);
        (int _, int[] slots, int[] levels) = rows.First(page => page.Entry == 8);
        Assert.Equal([5, 6, FormProgressFixture.UnlockedForm], slots);
        Assert.Equal([2, 1, 1], levels);
        Assert.Equal([5, 6, FormProgressFixture.UnlockedForm], session.Partners[0]!.EquippedForms);
    }

    /// <summary>Resolves a won reward-fixture battle through the production seam, crediting nothing yet.</summary>
    /// <param name="session">Persistent session.</param>
    /// <returns>The resolution and its settlement.</returns>
    private static BattleOutcomeResolution Resolve(PlayerSession session)
    {
        BattleFlowController flow = RewardSessionFixture.Build(session);
        RewardSessionFixture.Win(flow);
        return GameHost.BattleSetupCoordinator.ResolveOutcome(session, flow, () => BattleRewardSettlementTests.Context(Reward, new RewardSessionFixture.FixedRng(), null))
            ?? throw new InvalidOperationException("battle was not won");
    }

    /// <summary>Reads the report as a player: observes each page while its arrow waits, then confirms it with one Cross edge.</summary>
    /// <param name="report">Report under test.</param>
    /// <param name="observe">Check run with each waiting page's entry.</param>
    private static void ReadToEnd(FightReportController report, Action<int> observe)
    {
        bool waiting = false;
        for (int frame = 0; frame < 20_000 && !report.View.Done; frame++)
        {
            FightReportMessage? message = report.View.Message;
            bool arrow = message?.ArrowVisible == true;
            if (arrow && !waiting)
            {
                observe(message!.Entry);
            }

            waiting = arrow;
            report.Step(crossPressedEdge: arrow);
        }

        Assert.True(report.View.Done);
    }

    /// <summary>Asserts the reward fixture's awards were credited exactly once.</summary>
    /// <param name="session">Persistent session.</param>
    private static void AssertCreditedOnce(PlayerSession session)
    {
        Assert.Equal((40, 3, 3), (session.Partners[0]!.Growth!.Exp, session.Partners[0]!.Growth!.Level, session.Partners[0]!.BattleSeed.Stats.Level));
        Assert.Equal(50, session.Money);
        Assert.Equal(3, session.Inventory[RewardSessionFixture.DropItem]);
    }
}
