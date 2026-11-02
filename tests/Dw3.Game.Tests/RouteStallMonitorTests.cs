using System.Numerics;
using Dw3Production.Diagnostics.Field;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class RouteStallMonitorTests
{
    [Fact]
    public void OnlySnapshotTicksAreChecked()
    {
        var monitor = new RouteStallMonitor(Vector2.Zero);

        Assert.False(monitor.HasStalled(0, Vector2.Zero));
        Assert.False(monitor.HasStalled(149, Vector2.Zero));
        Assert.Equal(0, monitor.SnapshotTick);
    }

    [Fact]
    public void LessThanHalfAUnitOfProgressIsAStall()
    {
        var monitor = new RouteStallMonitor(Vector2.Zero);

        Assert.True(monitor.HasStalled(150, new Vector2(0.4f, 0)));
        Assert.Equal(0, monitor.SnapshotTick);
    }

    [Fact]
    public void ProgressMovesTheSnapshotForward()
    {
        var monitor = new RouteStallMonitor(Vector2.Zero);

        Assert.False(monitor.HasStalled(150, new Vector2(3, 0)));
        Assert.Equal(150, monitor.SnapshotTick);
        Assert.True(monitor.HasStalled(300, new Vector2(3.2f, 0)));
    }
}
