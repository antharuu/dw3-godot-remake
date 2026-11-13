using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

/// <summary>Verifies that terminal rewards reach the persistent session, not only the report.</summary>
public sealed class BattleRewardSettlementTests
{
    /// <summary>A won session battle with a certain enemy drop adds that item and the money to the session.</summary>
    [Fact]
    public void WonSessionBattle_CreditsTheSelectedDropToTheSessionInventory()
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int> { [RewardSessionFixture.HealItem] = 1, [RewardSessionFixture.DropItem] = 2 });
        BattleFlowController flow = RewardSessionFixture.Build(session);
        RewardSessionFixture.Win(flow);

        BattleOutcomeResolution resolution = Resolve(session, flow, new BattleReward(DigimonExp: 0, Exp: 0, Money: 50));

        Assert.Equal(RewardSessionFixture.DropItem, resolution.FightReport.Item);
        Assert.Equal(3, session.Inventory[RewardSessionFixture.DropItem]);
        Assert.Equal(50, resolution.FightReport.Money);
        Assert.Equal(50, session.Money);
    }

    /// <summary>A forced prize is credited instead of the enemy drop, after the drop's two source rolls.</summary>
    [Fact]
    public void ForcedPrize_CreditsThePrizeAndKeepsTheCandidateAndChanceRolls()
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int>());
        BattleFlowController flow = RewardSessionFixture.Build(session);
        RewardSessionFixture.Win(flow);
        var random = new RewardSessionFixture.FixedRng();

        BattleOutcomeResolution resolution = Resolve(session, flow, new BattleReward(0, 0, 0), random, RewardSessionFixture.PrizeItem);

        Assert.Equal(RewardSessionFixture.PrizeItem, resolution.FightReport.Item);
        Assert.Equal(1, session.Inventory[RewardSessionFixture.PrizeItem]);
        Assert.False(session.Inventory.ContainsKey(RewardSessionFixture.DropItem));
        Assert.Equal(2, random.DrawCount);
    }

    /// <summary>Defeat and both flights commit resources but never read the reward or its random stream.</summary>
    /// <param name="outcome">Non-winning terminal outcome.</param>
    [Theory]
    [InlineData(BattleOutcome.Lost)]
    [InlineData(BattleOutcome.PlayerFled)]
    [InlineData(BattleOutcome.EnemyFled)]
    public void NonWinningOutcome_DeliversNoRewardAndDrawsNoRewardRoll(BattleOutcome outcome)
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int> { [RewardSessionFixture.DropItem] = 2 });
        BattleFlowController flow = RewardSessionFixture.Build(session);
        flow.State.Players[0]!.Hp = 33;
        flow.State.Fought[0] = true;
        flow.EndBattle(outcome);

        BattleOutcomeResolution? resolution = GameHost.BattleSetupCoordinator.ResolveOutcome(
            session,
            flow,
            () => throw new Xunit.Sdk.XunitException("a non-winning outcome read the reward"));

        Assert.Null(resolution);
        Assert.Equal(0, session.Money);
        Assert.Equal(2, session.Inventory[RewardSessionFixture.DropItem]);
        Assert.Equal(0, session.Partners[0]!.Growth!.Exp);
        Assert.Equal(33, session.Partners[0]!.BattleSeed.Hp);
    }

    /// <summary>A zero reward with no drop changes neither money, inventory nor growth.</summary>
    [Fact]
    public void ZeroRewardWithoutDrop_LeavesTheSessionUnchanged()
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int> { [RewardSessionFixture.HealItem] = 1 });
        BattleFlowController flow = RewardSessionFixture.Build(session, item: 0);
        RewardSessionFixture.Win(flow);
        var random = new RewardSessionFixture.FixedRng();

        BattleOutcomeResolution resolution = Resolve(session, flow, new BattleReward(0, 0, 0), random);

        Assert.Equal(0, resolution.FightReport.Item);
        Assert.Equal(0, session.Money);
        Assert.Equal(new Dictionary<int, int> { [RewardSessionFixture.HealItem] = 1 }, session.Inventory);
        Assert.Equal(1, session.Partners[0]!.Growth!.Level);
        Assert.Equal(0, random.DrawCount);
    }

    /// <summary>Money stops at 9,999,999 and an item stock at 99, as the source report clamps them.</summary>
    /// <param name="money">Money owned before the battle.</param>
    /// <param name="stock">Copies of the dropped item owned before the battle.</param>
    /// <param name="expectedMoney">Money owned after the reward.</param>
    /// <param name="expectedStock">Copies owned after the reward.</param>
    [Theory]
    [InlineData(9_999_970, 98, 9_999_999, 99)]
    [InlineData(9_999_999, 99, 9_999_999, 99)]
    [InlineData(9_999_949, 0, 9_999_999, 1)]
    public void NearAndFullCaps_ClampMoneyAndItemStockAsTheSource(int money, int stock, int expectedMoney, int expectedStock)
    {
        PlayerSession session = new(
            [RewardSessionFixture.Partner(1), RewardSessionFixture.Partner(2), RewardSessionFixture.Partner(3)],
            new Dictionary<int, int> { [RewardSessionFixture.DropItem] = stock },
            money);
        BattleFlowController flow = RewardSessionFixture.Build(session);
        RewardSessionFixture.Win(flow);

        BattleOutcomeResolution resolution = Resolve(session, flow, new BattleReward(0, 0, 50));

        Assert.Equal(50, resolution.FightReport.Money);
        Assert.Equal(expectedMoney, session.Money);
        Assert.Equal(expectedStock, session.Inventory[RewardSessionFixture.DropItem]);
    }

    /// <summary>The money and experience equipment bonuses reach the session in the qualified integer order.</summary>
    [Fact]
    public void EquipmentBonuses_ReachTheSessionAndMatchTheReport()
    {
        PlayerSession session = new(
            [
                RewardSessionFixture.Partner(1, [BattleRewardEquipment.ExperienceBonusItem, BattleRewardEquipment.MoneyBonusItem]),
                RewardSessionFixture.Partner(2),
                RewardSessionFixture.Partner(3),
            ],
            new Dictionary<int, int>());
        BattleFlowController flow = RewardSessionFixture.Build(session);
        RewardSessionFixture.Win(flow);

        BattleOutcomeResolution resolution = Resolve(session, flow, new BattleReward(DigimonExp: 0, Exp: 9, Money: 54));

        Assert.Equal(64, resolution.FightReport.Money);
        Assert.Equal(64, session.Money);
        Assert.Equal([10, 0, 0], resolution.Report.Partners.Select(partner => partner.ExpGained));
        Assert.Equal(10, session.Partners[0]!.Growth!.Exp);
    }

    /// <summary>The settlement refuses a battle that was not won instead of crediting it.</summary>
    [Fact]
    public void SettleVictory_RejectsANonWinningFlow()
    {
        PlayerSession session = RewardSessionFixture.CreateSession(new Dictionary<int, int>());
        BattleFlowController flow = RewardSessionFixture.Build(session);
        flow.EndBattle(BattleOutcome.Lost);

        Assert.Throws<InvalidOperationException>(() => BattleRewardSettlement.SettleVictory(session, flow, Context(new BattleReward(0, 0, 10), new RewardSessionFixture.FixedRng(), null)));
        Assert.Equal(0, session.Money);
    }

    /// <summary>Resolves a terminal battle through the same coordinator seam the game host calls.</summary>
    /// <param name="session">Session receiving the reward.</param>
    /// <param name="flow">Won flow.</param>
    /// <param name="reward">Encounter reward.</param>
    /// <param name="random">Optional counting stream.</param>
    /// <param name="prize">Optional forced prize.</param>
    /// <returns>The delivered resolution, every report stage committed.</returns>
    internal static BattleOutcomeResolution Resolve(PlayerSession session, BattleFlowController flow, BattleReward reward, IBattleRng? random = null, int? prize = null) =>
        (GameHost.BattleSetupCoordinator.ResolveOutcome(session, flow, () => Context(reward, random ?? new RewardSessionFixture.FixedRng(), prize))
        ?? throw new Xunit.Sdk.XunitException("a won battle resolved no reward")).CommitAll();

    /// <summary>Builds the victory context with real growth tables.</summary>
    /// <param name="reward">Encounter reward.</param>
    /// <param name="random">Reward stream.</param>
    /// <param name="prize">Optional forced prize.</param>
    /// <returns>Victory context.</returns>
    internal static BattleVictoryContext Context(BattleReward reward, IBattleRng random, int? prize) => new(
        reward,
        1,
        RewardSessionFixture.Fighters(),
        RewardSessionFixture.GrowthTables(),
        random,
        id => BattleTextCatalog.Tokens([], $"fighter-{id}"),
        new BattleTextCatalog(RewardSessionFixture.Text()),
        prize);
}
