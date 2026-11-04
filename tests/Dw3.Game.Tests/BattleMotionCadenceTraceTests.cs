using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies that the cadence evidence comparator rejects a changed published skeleton state.</summary>
public sealed class BattleMotionCadenceTraceTests
{
    /// <summary>Rejects a trace whose effective bone pose diverges despite matching frame cursor state.</summary>
    [Fact]
    public void Compare_RejectsDifferentEffectiveBonePose()
    {
        var expected = new BattleMotionCadenceTrace(
            "60",
            [new BattleMotionCadenceSample(10, 2, 21, 21, 1024, 20, 10, false, [Bone(1f)])]);
        var actual = new BattleMotionCadenceTrace(
            "30",
            [new BattleMotionCadenceSample(10, 2, 21, 21, 1024, 20, 10, false, [Bone(1.5f)])]);

        var difference = BattleMotionCadenceTrace.Compare(expected, actual);

        Assert.Equal("samples[0].bones[0].positionX", difference);
    }

    /// <summary>Creates a fully specified effective bone pose with one configurable origin component.</summary>
    /// <param name="originX">World transform origin X.</param>
    /// <returns>A source-bone transform suitable for deterministic cadence evidence.</returns>
    private static BattleMotionCadenceBone Bone(float originX) =>
        new(1, true, originX, 2f, 3f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f);
}
