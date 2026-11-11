using Dw3Production;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

public sealed class BlastProductionLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedBlast_CancelsTemporaryStateAndOrphanedReturn(bool switchPartner)
    {
        using var fixture = new BlastProductionFixture(995);
        BattleFlowController flow = fixture.Build();
        Trigger(flow);
        BattleFighter original = flow.State.ActivePlayerFighter!;
        Assert.Single(BlastQueueInspector.Events(flow), entry => entry.Kind == BattleScheduledEventKind.BlastReturn);
        if (switchPartner)
        {
            Assert.True(flow.Choose(BattleCommand.Switch, 1, new BlastProductionFixture.Rng()));
        }
        else
        {
            GameHost.BattleSetupCoordinator.CommitResolvedBattle(fixture.Session, flow);
        }

        Assert.False(original.Temporary);
        Assert.Equal(101, original.Id);
        Assert.Equal(0, original.BlastGauge);
        Assert.DoesNotContain(BlastQueueInspector.Events(flow), entry => entry.Kind == BattleScheduledEventKind.BlastReturn);
        Assert.Equal(300, original.Hp);
        BattleFlowController next = fixture.Build();
        Assert.Equal(101, next.State.Players[0]!.Id);
        Assert.Equal(10, next.State.Players[0]!.Stats.Stats[BattleStat.Attack]);
    }

    [Fact]
    public void KnockedOutBlast_WithReserveDoesNotHealOnExpiryOrKeepAnOrphanEvent()
    {
        using var fixture = new BlastProductionFixture(995);
        BattleFlowController flow = fixture.Build();
        Trigger(flow);
        BattleFighter original = flow.State.ActivePlayerFighter!;
        original.Hp = 1;
        for (int step = 0; step < 5 && original.Hp > 0; step++)
        {
            if (flow.Phase == BattleFlowPhase.EnemyTurn)
            {
                flow.EnemyAct(new BlastProductionFixture.Rng());
            }
            else
            {
                Assert.True(flow.Choose(BattleCommand.Attack, 0, new BlastProductionFixture.Rng()));
            }
        }

        Assert.Equal(0, original.Hp);
        Assert.Equal(BattleOutcome.None, flow.Outcome);
        Assert.False(original.Temporary);
        Assert.DoesNotContain(BlastQueueInspector.Events(flow), entry => entry.Kind == BattleScheduledEventKind.BlastReturn);
        Assert.True(flow.Choose(BattleCommand.Switch, 1, new BlastProductionFixture.Rng()));
        Assert.Equal(1, flow.State.ActivePlayer);
    }

    [Theory]
    [InlineData(0, 1, 110, 2000)]
    [InlineData(0, 25, 112, 2000)]
    [InlineData(1, 25, 112, 2500)]
    [InlineData(4, 70, 114, 4000)]
    public void DurationUsesPermanentPartnerOrdinalAndFormUsesPermanentLevel(int partner, int level, int form, int duration)
    {
        using var fixture = new BlastProductionFixture(995, partner, level);
        BattleFlowController flow = fixture.Build();
        int before = BlastQueueInspector.Scheduler(flow).Elapsed;
        Trigger(flow);
        BattleScheduledEvent expiry = Assert.Single(BlastQueueInspector.Events(flow), entry => entry.Kind == BattleScheduledEventKind.BlastReturn);
        Assert.Equal(form, flow.State.ActivePlayerFighter!.Id);
        Assert.Equal(101 + partner, flow.State.ActivePlayerFighter.PrevId);
        Assert.Equal(duration, expiry.Delay + BlastQueueInspector.Scheduler(flow).Elapsed - before);
    }

    [Theory]
    [InlineData(5, 2000)]
    [InlineData(6, 3160)]
    [InlineData(7, 3160)]
    public void LaterPartnerDefinitionsUseTheirRandomEventFamilies(int partner, int duration)
    {
        using var fixture = new BlastProductionFixture(995, partner);
        BattleFlowController flow = fixture.Build();
        Trigger(flow);
        BattleScheduledEvent expiry = Assert.Single(BlastQueueInspector.Events(flow), entry => entry.Kind == BattleScheduledEventKind.BlastReturn);
        Assert.Equal(duration, expiry.Delay + BlastQueueInspector.Scheduler(flow).Elapsed);
    }

    [Fact]
    public void PartnerDelayRetainsTheSourceEventStrengthRegister()
    {
        using var fixture = new BlastProductionFixture(995, 6);
        BattleFlowController flow = fixture.Build();
        Assert.True(flow.State.InflictConfusion(BattleSide.Player, 128, 17, new BlastProductionFixture.Rng()));
        Trigger(flow);
        BattleScheduledEvent expiry = Assert.Single(BlastQueueInspector.Events(flow), entry => entry.Kind == BattleScheduledEventKind.BlastReturn);
        Assert.Equal(3296, expiry.Delay + BlastQueueInspector.Scheduler(flow).Elapsed);
    }

    [Fact]
    public void SharedBaseSlotsKeepTheirOwnGaugeResourcesAndNormalEquipmentAcrossCommit()
    {
        using var fixture = new BlastProductionFixture(995, duplicateBases: true);
        fixture.Session.Partners[1]!.BattleSeed.BlastGauge = 345;
        BattleFlowController flow = fixture.Build();
        Trigger(flow);
        Assert.True(flow.Choose(BattleCommand.Switch, 1, new BlastProductionFixture.Rng()));
        Assert.Equal(345, flow.State.ActivePlayerFighter!.BlastGauge);
        GameHost.BattleSetupCoordinator.CommitResolvedBattle(fixture.Session, flow);
        BattleFlowController second = fixture.Build();
        Assert.Equal(0, second.State.Players[0]!.BlastGauge);
        Assert.Equal(345, second.State.Players[1]!.BlastGauge);
        Assert.Equal(300, second.State.Players[0]!.Hp);
        Assert.Equal(200, second.State.Players[1]!.Hp);
        Assert.Equal([101, 109], second.PlayerDigivolutions);
        Assert.Equal([109], fixture.Session.Partners[1]!.EquippedForms);
        Assert.Equal([101, 109], fixture.Session.Partners[1]!.KnownForms);
    }

    [Fact]
    public void ForcedFormInterruptionCancelsThePendingBlastReturn()
    {
        using var fixture = new BlastProductionFixture(995);
        BattleFlowController flow = fixture.Build();
        Trigger(flow);
        Assert.True(flow.State.TryQueueDedigivolve(BattleSide.Enemy, 128, new BlastProductionFixture.Rng()));
        Assert.True(flow.Choose(BattleCommand.Attack, 0, new BlastProductionFixture.Rng()));
        Assert.False(flow.State.Players[0]!.Temporary);
        Assert.Equal(0, flow.State.Players[0]!.BlastGauge);
        Assert.DoesNotContain(BlastQueueInspector.Events(flow), entry => entry.Kind == BattleScheduledEventKind.BlastReturn);
    }

    private static void Trigger(BattleFlowController flow)
    {
        var rng = new BlastProductionFixture.Rng();
        Assert.True(flow.Choose(BattleCommand.Attack, 0, rng));
        flow.EnemyAct(rng);
        Assert.True(flow.State.ActivePlayerFighter!.Temporary);
    }
}
