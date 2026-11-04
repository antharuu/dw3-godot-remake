using Dw3Production.Diagnostics.Battle;
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

    /// <summary>Rejects a trace when its requested interruption happens after the attack blend has ended.</summary>
    [Fact]
    public void AssertEquivalent_RejectsInterruptionAfterBlendEnds()
    {
        var lateInterruption = TraceWithLateInterruption();
        var trials = new Dictionary<int, IReadOnlyDictionary<int, BattleMotionCadenceTrace>>
        {
            [30] = new Dictionary<int, BattleMotionCadenceTrace> { [383] = lateInterruption },
            [60] = new Dictionary<int, BattleMotionCadenceTrace> { [383] = lateInterruption },
            [120] = new Dictionary<int, BattleMotionCadenceTrace> { [383] = lateInterruption },
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BattleMotionCadenceVerifier.AssertEquivalent(trials, [30, 60, 120], [383]));

        Assert.Contains("interruption was not armed during active blend", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Creates a fully specified effective bone pose with one configurable origin component.</summary>
    /// <param name="originX">World transform origin X.</param>
    /// <returns>A source-bone transform suitable for deterministic cadence evidence.</returns>
    private static BattleMotionCadenceBone Bone(float originX) =>
        new(1, true, originX, 2f, 3f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f);

    private static BattleMotionCadenceTrace TraceWithLateInterruption()
    {
        var samples = Enumerable.Range(1, 10)
            .Select(index => new BattleMotionCadenceSample(index * 10, 1, index, index, 0, 0, -1, index == 3, [Bone(index)]))
            .ToArray();
        return new BattleMotionCadenceTrace("60", samples);
    }
}
