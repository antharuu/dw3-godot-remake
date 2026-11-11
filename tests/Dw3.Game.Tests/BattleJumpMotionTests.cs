using Dw3Production.Presentation;
using Godot;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleJumpMotionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public void ReactionJumpReturnsToItsQualifiedHeight(int kind)
    {
        var home = new Vector3(0f, 2f, 20f);
        var jump = new BattleJumpMotion(home, kind, 0, false, home);
        for (int i = 0; i < 120 && !jump.IsComplete; i++)
        {
            jump.Step();
        }

        Assert.True(jump.IsComplete);
        Assert.Equal(kind == 6 ? 2f : 0f, jump.Position.Y);
        Assert.Equal(home.Z, jump.Position.Z);
    }

    [Fact]
    public void KindFourMovesForwardAndLandsAtItsAuthoredDistance()
    {
        var jump = new BattleJumpMotion(new Vector3(0f, 0f, 20f), kind: 4, distance: 512, enemy: false);

        jump.Step();
        Vector3 airborne = jump.Position;
        while (!jump.IsComplete)
        {
            jump.Step();
        }

        Assert.True(airborne.Y > 0f);
        Assert.True(airborne.Z < 20f);
        Assert.Equal(new Vector3(0f, 0f, -22f), jump.Position);
    }
}
