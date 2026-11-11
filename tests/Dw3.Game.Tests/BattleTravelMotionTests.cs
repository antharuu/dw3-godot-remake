using Dw3Production.Presentation;
using Godot;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleTravelMotionTests
{
    [Fact]
    public void TruncatedFixedPointStepLandsExactlyAndZeroTimeIsImmediate()
    {
        var target = new Vector3(2f, 3f, 4f);
        var move = new BattleTravelMotion(Vector3.Zero, target, 3);
        move.Step();
        Assert.InRange(move.Position.X, 0.66f, 0.67f);
        for (int i = 0; i < 4; i++)
        {
            move.Step();
        }

        Assert.True(move.IsComplete);
        Assert.Equal(target, move.Position);
        Assert.Equal(target, new BattleTravelMotion(Vector3.Zero, target, 0).Position);
    }
}
