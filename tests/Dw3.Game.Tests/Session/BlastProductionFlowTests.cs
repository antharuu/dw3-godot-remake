using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

public sealed class BlastProductionFlowTests
{
    [Fact]
    public void ProductionPack_PlayerDamageDoesNotChargeButReceivedDamageCharges()
    {
        using var fixture = new BlastProductionFixture();
        BattleFlowController flow = fixture.Build();
        var rng = new BlastProductionFixture.Rng();
        Assert.True(flow.Choose(BattleCommand.Attack, 0, rng));
        Assert.Equal(30, Assert.Single(flow.State.LastHitEvents).Damage);
        Assert.Equal(0, flow.State.ActivePlayerFighter!.BlastGauge);
        flow.EnemyAct(rng);
        Assert.Equal(30, Assert.Single(flow.State.LastHitEvents).Damage);
        Assert.Equal(270, flow.State.ActivePlayerFighter.Hp);
        Assert.Equal(5, flow.State.ActivePlayerFighter.BlastGauge);
    }

    [Fact]
    public void ProductionSession_NonTemporaryGaugePersistsIntoNextBattle()
    {
        using var fixture = new BlastProductionFixture(400);
        BattleFlowController first = fixture.Build();
        var rng = new BlastProductionFixture.Rng();
        Assert.True(first.Choose(BattleCommand.Attack, 0, rng));
        first.EnemyAct(rng);
        Dw3Production.GameHost.BattleSetupCoordinator.CommitResolvedBattle(fixture.Session, first);
        BattleFlowController second = fixture.Build();
        Assert.Equal(405, second.State.ActivePlayerFighter!.BlastGauge);
        Assert.Equal(270, second.State.ActivePlayerFighter.Hp);
    }

    [Fact]
    public void ProductionBlast_SignatureIsFreeDespiteInsufficientMp()
    {
        using var fixture = new BlastProductionFixture(995);
        fixture.Session.Partners[0]!.BattleSeed.Mp = 7;
        BattleFlowController flow = fixture.Build();
        Trigger(flow);
        Assert.Equal([4], flow.PlayerTechniques.Select(technique => technique.Id));
        Assert.Equal(50, flow.State.ActivePlayerFighter!.Stats.Stats[BattleStat.Attack]);
        Assert.True(flow.Choose(BattleCommand.Technique, 4, new BlastProductionFixture.Rng()));
        Assert.Equal(7, flow.State.ActivePlayerFighter.Mp);
    }

    [Fact]
    public void ProductionBlast_ReturnRestoresAlternateFormStatsActionsAndResourcesAcrossBattles()
    {
        using var fixture = new BlastProductionFixture(995);
        fixture.Session.Partners[0]!.BattleSeed.Id = 109;
        fixture.Session.Partners[0]!.BattleSeed.BlastGauge = 999;
        fixture.Session.Partners[0]!.BattleSeed.Mp = 7;
        BattleFlowController flow = fixture.Build();
        Trigger(flow);
        Assert.Equal(109, flow.State.ActivePlayerFighter!.PrevId);
        for (int index = 0; index < 10 && flow.State.ActivePlayerFighter.Temporary; index++)
        {
            Step(flow);
        }

        BattleFighter partner = flow.State.ActivePlayerFighter;
        Assert.False(partner.Temporary);
        Assert.Equal(109, partner.Id);
        Assert.Equal(20, partner.Stats.Stats[BattleStat.Attack]);
        Assert.Equal([2], flow.PlayerTechniques.Select(technique => technique.Id));
        int hp = partner.Hp;
        Dw3Production.GameHost.BattleSetupCoordinator.CommitResolvedBattle(fixture.Session, flow);
        BattleFlowController second = fixture.Build();
        Assert.False(second.State.ActivePlayerFighter!.Temporary);
        Assert.Equal(109, second.State.ActivePlayerFighter.Id);
        Assert.Equal(20, second.State.ActivePlayerFighter.Stats.Stats[BattleStat.Attack]);
        Assert.Equal(hp, second.State.ActivePlayerFighter.Hp);
        Assert.Equal(7, second.State.ActivePlayerFighter.Mp);
    }

    [Fact]
    public void ProductionPack_ReceivedHitTriggersTemporaryFormWithProductionStatsAndActions()
    {
        using var fixture = new BlastProductionFixture(995);
        BattleFlowController flow = fixture.Build();
        var rng = new BlastProductionFixture.Rng();
        Assert.True(flow.Choose(BattleCommand.Attack, 0, rng));
        flow.EnemyAct(rng);
        BattleFighter partner = flow.State.ActivePlayerFighter!;
        Assert.True(partner.Temporary);
        Assert.Equal(110, partner.Id);
        Assert.Equal(101, partner.PrevId);
        Assert.Equal(50, partner.Stats.Stats[BattleStat.Attack]);
        Assert.Equal(300, partner.Hp);
        Assert.Equal(100, partner.Mp);
        Assert.Contains(flow.PlayerTechniques, technique => technique.Id == 4);
    }

    [Fact]
    public void ThresholdQueuesTransformationBeforeTheNormalSchedulerConsumesIt()
    {
        using var fixture = new BlastProductionFixture(995);
        BattleFlowController flow = fixture.Build();
        flow.State.TickBlastGauge(30, new BlastProductionFixture.Rng());
        Assert.False(flow.State.ActivePlayerFighter!.Temporary);
        Assert.Equal(1000, flow.State.ActivePlayerFighter.BlastGauge);
        Assert.Contains(BlastQueueInspector.Events(flow), entry => entry.Kind == BattleScheduledEventKind.BlastStart);
        Assert.True(flow.Choose(BattleCommand.Attack, 0, new BlastProductionFixture.Rng()));
        Assert.True(flow.State.ActivePlayerFighter.Temporary);
        Assert.DoesNotContain(BlastQueueInspector.Events(flow), entry => entry.Kind == BattleScheduledEventKind.BlastStart);
        Assert.Single(BlastQueueInspector.Events(flow), entry => entry.Kind == BattleScheduledEventKind.BlastReturn);
    }

    [Fact]
    public void ProductionBlast_HealsAtTransformationAndClearsEuConfusionEvent()
    {
        using var fixture = new BlastProductionFixture(995);
        fixture.Session.Partners[0]!.BattleSeed.Mp = 7;
        BattleFlowController flow = fixture.Build();
        var rng = new BlastProductionFixture.Rng();
        Assert.True(flow.Choose(BattleCommand.Attack, 0, rng));
        Assert.True(flow.State.InflictConfusion(BattleSide.Enemy, 128, 23, rng));
        Assert.Contains(BlastQueueInspector.Events(flow), entry => entry.Kind == BattleScheduledEventKind.ConfusionEnd);
        flow.EnemyAct(rng);
        BattleFighter partner = flow.State.ActivePlayerFighter!;
        Assert.True(partner.Temporary);
        Assert.Equal(300, partner.Hp);
        Assert.Equal(7, partner.Mp);
        Assert.Equal(0, partner.Flags & FighterFlags.Confused);
        Assert.Equal(0, partner.Confusion);
        Assert.DoesNotContain(BlastQueueInspector.Events(flow), entry => entry.Kind == BattleScheduledEventKind.ConfusionEnd);
    }

    private static void Trigger(BattleFlowController flow)
    {
        var rng = new BlastProductionFixture.Rng();
        Assert.True(flow.Choose(BattleCommand.Attack, 0, rng));
        flow.EnemyAct(rng);
        Assert.True(flow.State.ActivePlayerFighter!.Temporary);
    }

    private static void Step(BattleFlowController flow)
    {
        var rng = new BlastProductionFixture.Rng();
        if (flow.Phase == BattleFlowPhase.EnemyTurn)
        {
            flow.EnemyAct(rng);
        }
        else
        {
            Assert.True(flow.Choose(BattleCommand.Attack, 0, rng));
        }
    }
}
