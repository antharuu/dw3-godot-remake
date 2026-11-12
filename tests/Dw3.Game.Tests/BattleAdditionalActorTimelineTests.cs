using Dw3.Data.Battle;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleAdditionalActorTimelineTests
{
    [Fact]
    public void FighterLifecycleIsNotRewrittenAsAnEffectModel()
    {
        var script = new BattleActionScript(
            12,
            [
                new(BattleActionOpcode.Model, [6, 2, 42]),
                new(BattleActionOpcode.Model, [4, 2, 0, 1024, 0, 0]),
                new(BattleActionOpcode.Model, [9, 2]),
                new(BattleActionOpcode.Fade, [0, 10]),
                new(BattleActionOpcode.End, []),
            ]);
        var timeline = new BattleActionTimeline(script, new ActionSequenceContext(BattleSide.Player, 10, 20, [], 12));
        var events = timeline.Advance();
        Assert.Equal(5, events.Count);
        Assert.Equal<short>([6, 2, 42], Assert.IsType<ModelControlEvent>(events[0]).Arguments);
        Assert.Equal<short>([4, 2, 0, 1024, 0, 0], Assert.IsType<ModelControlEvent>(events[1]).Arguments);
        Assert.Equal<short>([9, 2], Assert.IsType<ModelControlEvent>(events[2]).Arguments);
        Assert.IsType<ScreenFadeEvent>(events[3]);
        Assert.DoesNotContain(events, item => item is EffectModelEvent);
    }

    [Fact]
    public void MissingFadeBackendKeepsItsOwnCommandPending()
    {
        var script = new BattleActionScript(12, [new(BattleActionOpcode.Fade, [1, 10]), new(BattleActionOpcode.End, [])]);
        var timeline = new BattleActionTimeline(script, new ActionSequenceContext(BattleSide.Player, 10, 20, [], 12))
        {
            Acknowledge = request => request is not ScreenFadeEvent,
        };
        Assert.Empty(timeline.Advance(30));
        Assert.Equal(0, timeline.Cursor);
        Assert.False(timeline.IsComplete);
    }
}
