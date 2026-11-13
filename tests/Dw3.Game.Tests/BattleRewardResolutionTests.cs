using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies source-qualified terminal battle reward allocation.</summary>
public sealed class BattleRewardResolutionTests
{
    /// <summary>Excludes a knocked-out participant from the experience share and selects one eligible enemy drop.</summary>
    [Fact]
    public void ResolveVictory_KnockedOutParticipantIsIneligibleAndEligibleDropIsReported()
    {
        var state = new BattleState
        {
            Players = [Partner(1, 100), Partner(2, 0), null],
            Enemies = [Enemy(10, item: 42, itemChance: 1023), Enemy(11, item: 0, itemChance: 0), null],
            Fought = [true, true, false],
            ActivePlayer = 0,
            ActiveEnemy = 0,
            Outcome = BattleOutcome.Won,
        };
        var flow = new BattleFlowController();
        flow.Start(state);

        BattleOutcomeResolution resolution = BattleOutcomeResolver.ResolveVictory(new BattleOutcomeRequest
        {
            Flow = flow,
            Reward = new BattleReward(DigimonExp: 1, Exp: 100, Money: 50),
            Random = new SequenceRng(0, 1023),
            DigimonFor = _ => throw new Xunit.Sdk.XunitException("growth must not be read without tables"),
            NameFor = id => BattleTextCatalog.Tokens([], $"fighter-{id}"),
            Text = EmptyText(),
        });

        Assert.Equal([100, 0], resolution.Report.Partners.Select(partner => partner.ExpGained));
        Assert.Equal(42, resolution.FightReport.Item);
    }

    /// <summary>Preserves source drop rolls before overriding the result with the scripted prize.</summary>
    [Fact]
    public void ResolveVictory_ScriptedPrizeOverridesEnemyDropAndMoneyBonus()
    {
        var state = new BattleState
        {
            Players = [Partner(1, 100), null, null],
            Enemies = [Enemy(10, item: 42, itemChance: 1023), null, null],
            Fought = [true, false, false],
            ActivePlayer = 0,
            ActiveEnemy = 0,
            Outcome = BattleOutcome.Won,
        };
        var flow = new BattleFlowController();
        flow.Start(state);

        var random = new SequenceRng(0, 1023);
        BattleOutcomeResolution resolution = BattleOutcomeResolver.ResolveVictory(new BattleOutcomeRequest
        {
            Flow = flow,
            Reward = new BattleReward(DigimonExp: 0, Exp: 0, Money: 50),
            Random = random,
            DigimonFor = _ => throw new Xunit.Sdk.XunitException("growth must not be read without tables"),
            NameFor = id => BattleTextCatalog.Tokens([], $"fighter-{id}"),
            HasMoneyBonusEquipment = true,
            HasPrize = true,
            Prize = 99,
            Text = EmptyText(),
        });

        Assert.Equal(60, resolution.Report.Money);
        Assert.Equal(99, resolution.FightReport.Item);
        Assert.Equal(0, Assert.Single(resolution.Report.Partners).ExpGained);
        Assert.Equal(2, random.DrawCount);
    }

    /// <summary>Applies the source one-fifth EXP equipment bonus to its equipped participant only.</summary>
    [Fact]
    public void ResolveVictory_ExperienceBonusEquipmentAddsOneFifthForThatParticipant()
    {
        var state = new BattleState
        {
            Players = [Partner(1, 100), Partner(2, 100), null],
            Enemies = [Enemy(10, item: 0, itemChance: 0), null, null],
            Fought = [true, true, false],
            ActivePlayer = 0,
            ActiveEnemy = 0,
            Outcome = BattleOutcome.Won,
        };
        var flow = new BattleFlowController();
        flow.Start(state);

        BattleOutcomeResolution resolution = BattleOutcomeResolver.ResolveVictory(new BattleOutcomeRequest
        {
            Flow = flow,
            Reward = new BattleReward(DigimonExp: 0, Exp: 200, Money: 0),
            Random = new SequenceRng(),
            DigimonFor = _ => throw new Xunit.Sdk.XunitException("growth must not be read without tables"),
            NameFor = id => BattleTextCatalog.Tokens([], $"fighter-{id}"),
            HasExperienceBonusEquipment = index => index == 1,
            Text = EmptyText(),
        });

        Assert.Equal([120, 144], resolution.Report.Partners.Select(partner => partner.ExpGained));
        Assert.Equal(120, resolution.Report.Exp);
    }

    /// <summary>Builds the empty text catalogue required by a simulation-only reward test.</summary>
    /// <returns>A catalogue with no game text.</returns>
    private static BattleTextCatalog EmptyText() => new(new BattleTextData([], [], [], [], [], [], [], new Dictionary<string, BattleTextTemplate>(), new Dictionary<string, int>()));

    /// <summary>Creates a living or knocked-out player fighter without unrelated stats.</summary>
    private static BattleFighter Partner(int id, int hp) => new()
    {
        Id = id,
        Hp = hp,
        MaxHp = 100,
        Mp = 10,
        MaxMp = 10,
    };

    /// <summary>Creates an enemy holding an optional drop item.</summary>
    private static BattleFighter Enemy(int id, int item, int itemChance) => new()
    {
        Id = id,
        Hp = 100,
        MaxHp = 100,
        Mp = 10,
        MaxMp = 10,
        Item = item,
        ItemChance = itemChance,
    };

    /// <summary>Supplies deterministic source-order rolls.</summary>
    private sealed class SequenceRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        /// <summary>Gets the number of source random draws consumed by the resolution.</summary>
        public int DrawCount { get; private set; }

        /// <summary>Returns the next configured roll, or zero after exhaustion.</summary>
        /// <returns>The deterministic roll.</returns>
        public int NextValue()
        {
            DrawCount++;
            return _values.Count > 0 ? _values.Dequeue() : 0;
        }
    }
}
