using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleHudGaugeProjectionTests
{
    [Fact]
    public void Build_ProjectsTheActiveFighterBlastGaugeIndependentlyFromMp()
    {
        var fighter = new BattleFighter { Id = 383, Hp = 9999, MaxHp = 9999, Mp = 0, MaxMp = 99, BlastGauge = 1000 };
        var flow = new BattleFlowController();
        flow.Start(new BattleState { Players = [fighter], Enemies = [new BattleFighter { Id = 1, Hp = 1, MaxHp = 1 }] });

        var view = BattleViewBuilder.Build(flow, new BattleMenuView(BattleMenuKind.Root, 0, 0, 0, 0, [], null, null, null), null, Name);

        Assert.Equal(1000, view.Players[0].BlastGauge);
        Assert.Equal(0, view.Players[0].Mp);
    }

    [Fact]
    public void HpDisplay_SamplesThenEasesDamageForThirtySourceFrames()
    {
        var display = new BattleHudHpDisplay();
        var full = new BattleFighterView(1, null, 100, 100, 0, 0, true, false);
        var damaged = full with { Hp = 0 };

        Assert.Equal(100, display.Value(0, full, 0));
        Assert.Equal(100, display.Value(0, damaged, 133));
        Assert.InRange(display.Value(0, damaged, 383), 1, 99);
        Assert.Equal(0, display.Value(0, damaged, 633));
    }

    private static Dw3Production.Conversion.BattleTextLine Name(int id) => new([], [], $"name={id}");
}
