using Dw3Production.Presentation;
using Godot;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Tests source-raster silhouette measurements used by battle projection diagnostics.</summary>
public sealed class BattleSilhouetteMetricsTests
{
    [Fact]
    public void FromBoundsKeepsTheSourceAnchorAndLogicalProportions()
    {
        var metrics = BattleSilhouetteMetrics.FromBounds(new Rect2(80, 60, 64, 120));

        Assert.Equal(new Vector2(112, 180), metrics.Anchor);
        Assert.Equal(64f / 320f, metrics.WidthFraction, 5);
        Assert.Equal(120f / 240f, metrics.HeightFraction, 5);
    }

    [Fact]
    public void FromBoundsRejectsAnEmptySilhouette()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BattleSilhouetteMetrics.FromBounds(new Rect2(5, 5, 0, 20)));
    }
}
