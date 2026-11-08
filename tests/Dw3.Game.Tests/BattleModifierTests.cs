using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Tests battle-local stat modifiers, charge, and form-bound snapshots.</summary>
public sealed class BattleModifierTests
{
    [Fact]
    public void StatModifierCapsAtDoubleAndRefreshesTheResolvedStat()
    {
        BattleState state = CreateState();

        Assert.True(state.ApplyStatModifier(BattleSide.Player, 0, BattleStat.Attack, 128, new SequenceRng()));
        Assert.True(state.ApplyStatModifier(BattleSide.Player, 0, BattleStat.Attack, 128, new SequenceRng()));

        BattleFighter fighter = state.Players[0]!;
        Assert.Equal(50, fighter.Boosts[0]);
        Assert.Equal(100, fighter.Stats.Stats[BattleStat.Attack]);
    }

    [Fact]
    public void StatModifierCapsAtHalfBelowBaseAndDoesNotContaminatePartner()
    {
        BattleState state = CreateState();

        Assert.True(state.ApplyStatModifier(BattleSide.Player, 0, BattleStat.Defense, -128, new SequenceRng()));
        Assert.True(state.PlayerSwitch(1));

        Assert.Equal(-25, state.Players[0]!.Boosts[1]);
        Assert.Equal(25, state.Players[0]!.Stats.Stats[BattleStat.Defense]);
        Assert.Equal(50, state.Players[1]!.Stats.Stats[BattleStat.Defense]);
    }

    [Fact]
    public void ChargeChangesDamageAndIsConsumedByTheNextTechnique()
    {
        BattleState state = CreateState();
        var tech = new TechData { Power = 10, Icon = BattleConst.TechPhysical, Mp = 0, Accuracy = 128 };

        state.SetCharge(BattleSide.Player, 64);
        Assert.True(state.PlayerTechnique(tech, new SequenceRng()));

        Assert.Equal(80, state.Enemies[0]!.Hp);
        Assert.Equal(0, state.Players[0]!.Charge);
        Assert.Equal(0, state.Players[0]!.Stats.DamageBonus);
    }

    [Fact]
    public void FormChangeRefreshesCurrentFormWithoutDroppingBattleModifier()
    {
        BattleState state = CreateState(formAttack: 90);

        Assert.True(state.ApplyStatModifier(BattleSide.Player, 0, BattleStat.Attack, 64, new SequenceRng()));
        Assert.True(state.PlayerDigivolve(2));

        Assert.Equal(25, state.Players[0]!.Boosts[0]);
        Assert.Equal(115, state.Players[0]!.Stats.Stats[BattleStat.Attack]);
    }

    [Fact]
    public void StatModifierExpiresWithoutChangingThePartnerSnapshot()
    {
        BattleState state = CreateState();
        var tech = new TechData { Power = 1, Icon = BattleConst.TechPhysical, Accuracy = 128 };

        Assert.True(state.ApplyStatModifier(BattleSide.Player, 0, BattleStat.Attack, 64, new SequenceRng()));
        _ = state.PlayerAttack(tech, new SequenceRng());
        _ = state.PlayerAttack(tech, new SequenceRng());
        _ = state.PlayerAttack(tech, new SequenceRng());

        Assert.Equal(0, state.Players[0]!.Boosts[0]);
        Assert.Equal(50, state.Players[0]!.Stats.Stats[BattleStat.Attack]);
    }

    [Fact]
    public void ElementBoostExpiresThroughSchedulerAndRestoresThePreviousField()
    {
        BattleState state = CreateState();
        ElementBoost previous = new(2, 32);
        ElementBoost temporary = new(5, 64);
        var flow = new BattleFlowController
        {
            PlayerBasicTech = new TechData { Power = 1, Icon = BattleConst.TechPhysical, Accuracy = 128 },
        };
        state.Boost = previous;

        Assert.True(state.ApplyElementBoost(temporary, new SequenceRng()));
        Assert.Equal(temporary, state.Boost);
        flow.Start(state);
        flow.EnemyAct(new SequenceRng());
        Assert.True(flow.Choose(BattleCommand.Attack, 0, new SequenceRng()));
        flow.EnemyAct(new SequenceRng());
        Assert.True(flow.Choose(BattleCommand.Attack, 0, new SequenceRng()));

        Assert.Equal(previous, state.Boost);
        Assert.False(state.ApplyElementBoost(ElementBoost.None, new SequenceRng()));
        Assert.Equal(previous, state.Boost);
    }

    [Fact]
    public void SpecialStatusExpiryLeavesLaterSpecialAndFormSnapshotUntouched()
    {
        BattleState state = CreateState(formAttack: 90);
        var flow = new BattleFlowController
        {
            PlayerBasicTech = new TechData { Power = 1, Icon = BattleConst.TechPhysical, Accuracy = 128 },
        };

        Assert.True(state.ApplySpecialStatus(BattleSide.Player, 0, new SequenceRng()));
        Assert.True(state.ApplySpecialStatus(BattleSide.Player, 1, new SequenceRng(2000)));
        Assert.True(state.Players[0]!.Special);
        Assert.True(state.Players[1]!.Special);
        flow.Start(state);
        flow.EnemyAct(new SequenceRng());
        Assert.True(flow.Choose(BattleCommand.Digivolve, 2, new SequenceRng()));
        flow.EnemyAct(new SequenceRng());
        Assert.True(flow.Choose(BattleCommand.Attack, 0, new SequenceRng()));

        Assert.False(state.Players[0]!.Special);
        Assert.True(state.Players[1]!.Special);
        Assert.Equal(90, state.Players[0]!.Stats.Stats[BattleStat.Attack]);
        Assert.False(state.ApplySpecialStatus(BattleSide.Player, fighterSlot: 2, new SequenceRng()));
    }

    private static BattleState CreateState(int formAttack = 0)
    {
        var baseStats = new BattleStats();
        baseStats.Stats[BattleStat.Attack] = 50;
        baseStats.Stats[BattleStat.Defense] = 50;
        baseStats.Stats[BattleStat.Spirit] = 50;
        baseStats.Stats[BattleStat.Speed] = 50;
        var formStats = new BattleStats();
        formStats.Stats[BattleStat.Attack] = formAttack;
        formStats.Stats[BattleStat.Defense] = 50;
        formStats.Stats[BattleStat.Spirit] = 50;
        formStats.Stats[BattleStat.Speed] = 50;
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 20, MaxMp = 20, Stats = baseStats, FormStats = new Dictionary<int, BattleStats> { [2] = formStats } };
        setup.Partners[1] = new PartnerSeed { Id = 3, Hp = 100, MaxHp = 100, Mp = 20, MaxMp = 20, Stats = baseStats };
        setup.Enemies[0] = new BattleEnemy { Fighter = 4, Hp = 100, Mp = 20, Strength = 16, BaseStats = baseStats };
        return BattleState.InitFighters(setup, digimon: 1, new SequenceRng());
    }

    private sealed class SequenceRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        public int NextValue() => _values.Count > 0 ? _values.Dequeue() : 0;
    }
}
