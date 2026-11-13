using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

/// <summary>Verifies reward delivery across the terminal tick, the report and the next battle built from the same session.</summary>
public sealed class BattleRewardLifecycleTests
{
    /// <summary>The terminal guard, the report and repeated ticks deliver the reward exactly once.</summary>
    [Fact]
    public void TerminalTickAndReport_DeliverTheRewardExactlyOnce()
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int> { [RewardSessionFixture.DropItem] = 2 });
        BattleFlowController flow = RewardSessionFixture.Build(session);
        RewardSessionFixture.Win(flow);
        var host = new SettlingHost(flow, () => BattleRewardSettlementTests.Resolve(session, flow, new BattleReward(DigimonExp: 0, Exp: 40, Money: 50)));
        var coordinator = new BattleTickCoordinator(host);

        for (int tick = 0; tick < 4000 && !host.Ended; tick++)
        {
            coordinator.Tick();
        }

        coordinator.Tick();
        coordinator.Tick();
        host.StepFinishedReport();

        Assert.True(host.Ended);
        Assert.True(host.ReportDone);
        Assert.Equal(1, host.Settlements);
        Assert.Equal(50, session.Money);
        Assert.Equal(3, session.Inventory[RewardSessionFixture.DropItem]);
        Assert.Equal(40, session.Partners[0]!.Growth!.Exp);
        Assert.Equal(3, session.Partners[0]!.Growth!.Level);
        Assert.Equal(3, session.Partners[0]!.BattleSeed.Stats.Level);
    }

    /// <summary>
    /// The terminal guard, the report and repeated ticks credit used-form experience and an unlock exactly once.
    /// Ledger: reward 30, partner level 25, one used slot: 10, form 5 reaches level 2 and unlocks form 8.
    /// </summary>
    [Fact]
    public void TerminalTickAndReport_CreditFormProgressionExactlyOnce()
    {
        PlayerSession session = FormProgressFixture.Session(level: 25, [5, 6], [5, 6]);
        BattleFlowController flow = FormProgressFixture.Build(session);
        FormProgressFixture.FightAs(flow, [5]);
        DigivolutionEntry[] tree = [new DigivolutionEntry(0, FormProgressFixture.UnlockedForm, [new EvolutionNeed(0, 5, 2)], 0, 0)];
        var host = new SettlingHost(flow, () => GameHost.BattleSetupCoordinator.ResolveOutcome(session, flow, () => FormProgressFixture.Victory(30, tree)) ?? throw new InvalidOperationException("battle was not won"));
        var coordinator = new BattleTickCoordinator(host);

        for (int tick = 0; tick < 4000 && !host.Ended; tick++)
        {
            coordinator.Tick();
        }

        coordinator.Tick();
        coordinator.Tick();
        host.StepFinishedReport();

        PlayerSessionPartner partner = Assert.IsType<PlayerSessionPartner>(session.Partners[0]);
        Assert.True(host.ReportDone);
        Assert.Equal(1, host.Settlements);
        Assert.Equal((2, 10), (partner.FormProgress(5).Level, partner.FormProgress(5).Exp));
        Assert.Equal([FormProgressFixture.AcquiredTechnique], partner.FormProgress(5).Techniques);
        Assert.Equal(1, partner.KnownForms.Count(form => form == FormProgressFixture.UnlockedForm));
        Assert.Equal([5, 6, FormProgressFixture.UnlockedForm], partner.EquippedForms);
    }

    /// <summary>A second battle built from the session starts from the delivered stock, money, levels, maxima and stats.</summary>
    [Fact]
    public void SecondBattle_StartsFromTheDeliveredRewardAndGrowth()
    {
        PlayerSession session = new(
            [
                RewardSessionFixture.Partner(1, [BattleRewardEquipment.ExperienceBonusItem, BattleRewardEquipment.MoneyBonusItem]),
                RewardSessionFixture.Partner(2),
                RewardSessionFixture.Partner(3),
            ],
            new Dictionary<int, int> { [RewardSessionFixture.HealItem] = 1 });
        BattleFlowController first = RewardSessionFixture.Build(session);
        RewardSessionFixture.Win(first);
        first.State.Fought[2] = true;
        first.State.Players[2]!.Hp = 0;

        BattleOutcomeResolution resolution = BattleRewardSettlementTests.Resolve(session, first, new BattleReward(DigimonExp: 0, Exp: 100, Money: 50));
        BattleFlowController second = RewardSessionFixture.Build(session);

        Assert.Equal([120, 0, 0], resolution.Report.Partners.Select(partner => partner.ExpGained));
        Assert.Equal(4, resolution.FightReport.Partners[0].LevelAfter);
        Assert.Equal(60, session.Money);
        Assert.Equal(1, second.ItemStock[RewardSessionFixture.DropItem]);
        Assert.Same(session.Inventory, second.ItemStock);
        Assert.Equal([BattleRewardEquipment.ExperienceBonusItem, BattleRewardEquipment.MoneyBonusItem], session.Partners[0]!.EquipmentIds);

        BattleFighter grown = Assert.IsType<BattleFighter>(second.State.Players[0]);
        Assert.Equal(4, grown.Stats.Level);
        Assert.Equal(121, grown.MaxHp);
        Assert.Equal(19, grown.MaxMp);
        Assert.Equal(100, grown.Hp);
        Assert.Equal(7, grown.Mp);
        Assert.Equal([19, 19, 19, 19, 109], grown.Stats.Stats);
        Assert.Equal([3, 3, 3, 3, 3, 3, 3], grown.Stats.Resist.Take(7));
        Assert.Equal(new PartnerGrowthState { Level = 4, Exp = 120, Tp = 15, MaxHp = 121, MaxMp = 19, BattleStats = [19, 19, 19, 19, 109, 14], Resistances = [3, 3, 3, 3, 3, 3, 3] }, session.Partners[0]!.Growth!, GrowthComparer.Instance);

        AssertUngrown(second.State.Players[1], session.Partners[1]!, hp: 100);
        AssertUngrown(second.State.Players[2], session.Partners[2]!, hp: 0);
    }

    /// <summary>Asserts that a nonparticipant or knocked-out partner kept its level-one sources.</summary>
    /// <param name="fighter">Fighter in the second battle.</param>
    /// <param name="partner">Session partner.</param>
    /// <param name="hp">Expected committed HP.</param>
    private static void AssertUngrown(BattleFighter? fighter, PlayerSessionPartner partner, int hp)
    {
        BattleFighter actual = Assert.IsType<BattleFighter>(fighter);
        Assert.Equal(hp, actual.Hp);
        Assert.Equal(100, actual.MaxHp);
        Assert.Equal(10, actual.MaxMp);
        Assert.Equal(1, partner.BattleSeed.Stats.Level);
        Assert.Equal([10, 10, 10, 10, 100], partner.BattleSeed.Stats.Stats);
        Assert.Equal(0, partner.Growth!.Exp);
        Assert.Equal(1, partner.Growth.Level);
    }

    /// <summary>Compares growth states by value.</summary>
    private sealed class GrowthComparer : IEqualityComparer<PartnerGrowthState>
    {
        /// <summary>Gets the shared comparer.</summary>
        public static GrowthComparer Instance { get; } = new();

        /// <inheritdoc/>
        public bool Equals(PartnerGrowthState? x, PartnerGrowthState? y) =>
            x is not null && y is not null
            && (x.Level, x.Exp, x.Tp, x.MaxHp, x.MaxMp) == (y.Level, y.Exp, y.Tp, y.MaxHp, y.MaxMp)
            && x.BattleStats.SequenceEqual(y.BattleStats)
            && x.Resistances.SequenceEqual(y.Resistances);

        /// <inheritdoc/>
        public int GetHashCode(PartnerGrowthState obj) => obj.Level;
    }

    /// <summary>Tick host whose terminal callback uses the production settlement seam and whose report is the real controller.</summary>
    /// <param name="flow">Won battle flow.</param>
    /// <param name="resolve">Production settlement call made by the terminal callback.</param>
    private sealed class SettlingHost(BattleFlowController flow, Func<BattleOutcomeResolution> resolve) : IBattleTickHost
    {
        private FightReportController? _report;
        private BattleOutcomeResolution? _resolution;

        /// <summary>Gets the number of times the terminal callback delivered a reward.</summary>
        public int Settlements { get; private set; }

        /// <summary>Gets a value indicating whether the host returned to the field.</summary>
        public bool Ended { get; private set; }

        /// <summary>Gets a value indicating whether the real report reached its end.</summary>
        public bool ReportDone => _report?.View.Done == true;

        /// <inheritdoc/>
        public BattleFlowController? Flow => Ended ? null : flow;

        /// <inheritdoc/>
        public bool HasFightReport => _report is not null;

        /// <inheritdoc/>
        public bool IsPlayerCameraActive => false;

        /// <inheritdoc/>
        public bool IsBattleResolved { get; private set; }

        /// <summary>Steps the finished report again, as a repeated completion input would.</summary>
        public void StepFinishedReport()
        {
            for (int step = 0; step < 10; step++)
            {
                _report!.Step(true);
            }
        }

        /// <inheritdoc/>
        public void ResolveOutcome()
        {
            _resolution = resolve();
            Settlements++;
        }

        /// <inheritdoc/>
        public void MarkBattleResolved() => IsBattleResolved = true;

        /// <inheritdoc/>
        public bool AdvanceVictoryFade() => true;

        /// <inheritdoc/>
        public void BeginFightReport() => _report = new FightReportController(_resolution!.FightReport);

        /// <inheritdoc/>
        public void TickFightReport()
        {
            _report!.Step(true);
            if (_report.View.Done)
            {
                EndBattle();
            }
        }

        /// <inheritdoc/>
        public void EndBattle() => Ended = true;

        /// <inheritdoc/>
        public void ActivatePlayerCamera()
        {
        }

        /// <inheritdoc/>
        public void DeactivatePlayerCamera()
        {
        }

        /// <inheritdoc/>
        public void TickBattleMenu()
        {
        }

        /// <inheritdoc/>
        public void EnemyAct()
        {
        }

        /// <inheritdoc/>
        public void TickActionSequence()
        {
        }

        /// <inheritdoc/>
        public void RefreshModels()
        {
        }
    }
}
