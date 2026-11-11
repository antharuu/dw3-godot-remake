using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

public sealed class BlastReceivedDamageTests
{
    [Theory]
    [InlineData(30, 300, 5)]
    [InlineData(30, 600, 1)]
    [InlineData(22, 300, 2)]
    [InlineData(0, 300, 0)]
    [InlineData(30, 0, 0)]
    [InlineData(-30, 300, 0)]
    [InlineData(1000, 100, 1000)]
    public void GainUsesIntegerPercentageBeforeSquaring(int damage, int hp, int expected)
    {
        Assert.Equal(expected, BattleBlastGaugeRules.ComputeGain(damage, hp));
    }

    [Theory]
    [InlineData(0x149, 0x14A, 11)]
    [InlineData(0x14A, 0x149, 10)]
    [InlineData(0x149, 0x149, 9)]
    [InlineData(0x14A, 0x14A, 12)]
    [InlineData(0, 0x149, 8)]
    public void AccessoryRoundingFollowsEquippedOrder(int first, int second, int expected)
    {
        Assert.Equal(expected, BattleBlastGaugeRules.ComputeGain(36, 300, [first, second]));
    }

    [Theory]
    [InlineData(0x149, 0x14A, 8)]
    [InlineData(0x14A, 0x149, 8)]
    public void ProductionEquipmentSlotsFeedReceivedCharge(int first, int second, int expected)
    {
        using var fixture = new BlastProductionFixture(equipment: [0, 0, 0, 0, first, second]);
        BattleFlowController flow = fixture.Build();
        var rng = new BlastProductionFixture.Rng();
        Assert.True(flow.Choose(BattleCommand.Attack, 0, rng));
        flow.EnemyAct(rng);
        Assert.Equal(expected, flow.State.ActivePlayerFighter!.BlastGauge);
    }

    [Fact]
    public void NonlethalEnemyMultiHitChargesFromTheQualifiedActionTotal()
    {
        using var fixture = new BlastProductionFixture();
        BattleFlowController flow = fixture.Build();
        var tech = new TechData { Accuracy = 128, Power = 30, Effect = BattleTechniqueEffect.MultiHit, HitCount = 2 };
        Assert.True(flow.State.EnemyTechnique(tech, new BlastProductionFixture.Rng()));
        Assert.Equal(2, flow.State.LastHitEvents.Count);
        Assert.Equal(240, flow.State.ActivePlayerFighter!.Hp);
        Assert.Equal(20, flow.State.ActivePlayerFighter.BlastGauge);
    }

    [Fact]
    public void EnemyCounterUsesItsReceivedActionDamageForTheGaugeContract()
    {
        using var fixture = new BlastProductionFixture();
        BattleFlowController flow = fixture.Build();
        flow.State.EnemyCounter(new TechData { Accuracy = 128, Power = 1, EffectPower = 32 }, 60, new BlastProductionFixture.Rng());
        Assert.Equal(30, Assert.Single(flow.State.LastHitEvents).Damage);
        Assert.Equal(270, flow.State.ActivePlayerFighter!.Hp);
        Assert.Equal(20, flow.State.ActivePlayerFighter.BlastGauge);
    }

    [Fact]
    public void LethalEnemyDamageDoesNotChargeOrTriggerBlast()
    {
        using var fixture = new BlastProductionFixture(995);
        fixture.Session.Partners[0]!.BattleSeed.Hp = 30;
        BattleFlowController flow = fixture.Build();
        Assert.True(flow.Choose(BattleCommand.Attack, 0, new BlastProductionFixture.Rng()));
        flow.EnemyAct(new BlastProductionFixture.Rng());
        Assert.Equal(0, flow.State.Players[0]!.Hp);
        Assert.False(flow.State.Players[0]!.Temporary);
        Assert.Equal(995, flow.State.Players[0]!.BlastGauge);
        Assert.Equal(BattleOutcome.None, flow.Outcome);
    }

    [Fact]
    public void FixedPartnerDurationAddsNoRngDrawAndTemporaryHitsDoNotChargeAgain()
    {
        using var below = new BlastProductionFixture();
        using var crossing = new BlastProductionFixture(995);
        BattleFlowController normal = below.Build();
        BattleFlowController blast = crossing.Build();
        var normalRng = new BlastProductionFixture.Rng();
        var blastRng = new BlastProductionFixture.Rng();
        Assert.True(normal.Choose(BattleCommand.Attack, 0, normalRng));
        Assert.True(blast.Choose(BattleCommand.Attack, 0, blastRng));
        normal.EnemyAct(normalRng);
        blast.EnemyAct(blastRng);
        Assert.Equal(normalRng.Calls, blastRng.Calls);
        Assert.True(blast.State.ActivePlayerFighter!.Temporary);
        BattleBlastResolver.TryTickAndTrigger(blast.State, 0, blast.State.ActivePlayerFighter, 30, BlastQueueInspector.Scheduler(blast), blastRng);
        Assert.Equal(1000, blast.State.ActivePlayerFighter.BlastGauge);
        Assert.Single(BlastQueueInspector.Events(blast), entry => entry.Kind == BattleScheduledEventKind.BlastReturn);
    }
}
