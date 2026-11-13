using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>GTE perspective transfer saturation and GPU span limits applied to source model polygons.</summary>
public sealed class BattleGteProjectionTests
{
    private const int Projection = 280;

    [Fact]
    public void CornerInFront_ProjectsLikeThePerspectiveDivision()
    {
        Assert.False(BattleGteProjection.Overflows(560, Projection));
        Assert.Equal((160 + 50, 120 - 25), BattleGteProjection.Project(100, -50, 560, Projection));
    }

    [Theory]
    [InlineData(140)]
    [InlineData(10)]
    [InlineData(0)]
    [InlineData(-300)]
    public void CornerWithinHalfTheProjectionOrBehind_OverflowsAndSaturates(int depth)
    {
        Assert.True(BattleGteProjection.Overflows(depth, Projection));
        Assert.Equal((0x3FF, -0x400), BattleGteProjection.Project(4000, -4000, depth, Projection));
    }

    [Fact]
    public void CornerJustBeyondHalfTheProjection_DoesNotOverflow() =>
        Assert.False(BattleGteProjection.Overflows(141, Projection));

    [Fact]
    public void Gpu_DrawsSpansUpToItsLimits_AndRefusesWiderOrTaller()
    {
        Assert.True(BattleGteProjection.GpuDraws([(0, 0), (1023, 0), (0, 511)]));
        Assert.False(BattleGteProjection.GpuDraws([(0, 0), (1024, 0), (0, 10)]));
        Assert.False(BattleGteProjection.GpuDraws([(0, 0), (10, 0), (0, 512)]));
    }

    [Fact]
    public void PolygonCrossingTheEye_IsRefused()
    {
        var corners = new[]
        {
            BattleGteProjection.Project(-900, 200, 600, Projection),
            BattleGteProjection.Project(900, 200, -50, Projection),
            BattleGteProjection.Project(0, -400, 30, Projection),
        };

        Assert.False(BattleGteProjection.GpuDraws(corners));
    }
}
