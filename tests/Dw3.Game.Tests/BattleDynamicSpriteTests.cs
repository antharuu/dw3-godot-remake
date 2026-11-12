using Dw3.Data.Battle;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleDynamicSpriteTests
{
    [Theory]
    [InlineData(7)]
    [InlineData(23)]
    public void DynamicSpriteUsesFrozenTechniqueResourceForLoadAndStart(int effect)
    {
        var tech = new TechData { Script = 8, ScriptEffect = effect };
        var context = ActionSequenceContext.TryCapture(BattleSide.Player, tech, 10, 20, []);
        Assert.NotNull(context);
        tech.ScriptEffect = 42;
        var script = new BattleActionScript(
            8,
            [
            new(BattleActionOpcode.SpriteEffect, [1, 9999]),
            new(BattleActionOpcode.SpriteEffect, [0, 9999, 12, -34, 56]),
            new(BattleActionOpcode.End, []),
        ]);
        var timeline = new BattleActionTimeline(script, context);
        var sprites = timeline.Advance().OfType<SpriteEffectEvent>().ToArray();
        Assert.Equal<short>([1, (short)effect], sprites[0].Arguments);
        Assert.Equal<short>([0, (short)effect, 12, -34, 56], sprites[1].Arguments);
        Assert.Equal(9999, script.Commands[1].Arguments[1]);
        Assert.True(timeline.IsComplete);
    }

    [Fact]
    public void ExplicitSpriteIsNotReplacedByTechniqueResource()
    {
        var context = new ActionSequenceContext(BattleSide.Enemy, 10, 20, [], 6) { ScriptEffect = 7 };
        var script = new BattleActionScript(
            6,
            [
            new(BattleActionOpcode.SpriteEffect, [1, 23]),
            new(BattleActionOpcode.End, []),
        ]);
        var timeline = new BattleActionTimeline(script, context);
        Assert.Equal(23, Assert.Single(timeline.Advance().OfType<SpriteEffectEvent>()).Arguments[1]);
    }
}
