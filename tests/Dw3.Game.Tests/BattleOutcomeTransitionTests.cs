using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies terminal outcomes and the mandatory replacement path after a knock-out.</summary>
public sealed class BattleOutcomeTransitionTests
{
    /// <summary>Verifies that a knocked-out active partner can only spend the turn by bringing in a living reserve.</summary>
    [Fact]
    public void Choose_KnockedOutActivePartnerWithLivingReserve_OnlyAllowsForcedSwitch()
    {
        var flow = new BattleFlowController();
        flow.Start(CreateState(withPlayerReserve: true));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        active.Hp = 0;
        active.Flags = FighterFlags.NoSwitch | FighterFlags.Confused;

        IReadOnlyList<BattleMenuLine> lines = flow.RootMenuLines();

        Assert.All(lines.Where((_, index) => index != (int)BattleCommand.Switch), line => Assert.False(line.Enabled));
        Assert.True(lines[(int)BattleCommand.Switch].Enabled);
        Assert.True(flow.Choose(BattleCommand.Switch, 1, new SequenceRng()));
        Assert.Equal(1, flow.State.ActivePlayer);
        Assert.Equal(2, Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter).Id);
        Assert.Equal(BattleOutcome.None, flow.Outcome);
        Assert.NotEqual(BattleFlowPhase.Ended, flow.Phase);
    }

    /// <summary>Verifies that knocking out the final active partner ends the battle as a defeat.</summary>
    [Fact]
    public void EnemyAttack_KnocksOutFinalPartner_SetsLostOutcome()
    {
        BattleState state = CreateState(withPlayerReserve: false);

        state.EnemyAttack(new TechData { Accuracy = 128, Power = 10000 }, new SequenceRng(0, 0));

        Assert.Equal(BattleOutcome.Lost, state.Outcome);
        Assert.True(Assert.IsType<BattleFighter>(state.Players[0]).IsKnockedOut);
    }

    /// <summary>Verifies that player and enemy departures remain distinct terminal outcomes.</summary>
    [Fact]
    public void RunAway_RecordsTheSideThatLeft()
    {
        BattleState playerState = CreateState(withPlayerReserve: false);
        BattleState enemyState = CreateState(withPlayerReserve: false);

        Assert.True(playerState.PlayerFlee(new SequenceRng(0)));
        Assert.True(enemyState.EnemyRunAway(new SequenceRng(0)));

        Assert.Equal(BattleOutcome.PlayerFled, playerState.Outcome);
        Assert.Equal(BattleOutcome.EnemyFled, enemyState.Outcome);
    }

    /// <summary>Creates a deterministic state with an optional living partner reserve.</summary>
    private static BattleState CreateState(bool withPlayerReserve)
    {
        var setup = new BattleSetup();
        setup.Partners[0] = Partner(1);
        setup.Partners[1] = withPlayerReserve ? Partner(2) : null;
        setup.Enemies[0] = Enemy(3);
        return BattleState.InitFighters(setup, digimon: 1, new SequenceRng(127));
    }

    /// <summary>Creates a living partner seed with neutral fight stats.</summary>
    private static PartnerSeed Partner(int id) => new()
    {
        Id = id,
        Hp = 100,
        MaxHp = 100,
        Mp = 100,
        MaxMp = 100,
        Stats = Stats(),
    };

    /// <summary>Creates a living enemy seed with neutral fight stats.</summary>
    private static BattleEnemy Enemy(int id) => new()
    {
        Fighter = id,
        Hp = 100,
        Mp = 100,
        Strength = 16,
        BaseStats = Stats(),
    };

    /// <summary>Creates neutral stats with equal speed for deterministic flee calculations.</summary>
    private static BattleStats Stats()
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Speed] = 100;
        return stats;
    }

    /// <summary>Supplies a fixed sequence of battle random values.</summary>
    private sealed class SequenceRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        /// <summary>Returns the next configured value, or zero after the sequence is exhausted.</summary>
        /// <returns>The next deterministic random value.</returns>
        public int NextValue() => _values.Count > 0 ? _values.Dequeue() : 0;
    }
}
