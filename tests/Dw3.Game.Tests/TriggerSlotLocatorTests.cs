using System.Numerics;
using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class TriggerSlotLocatorTests
{
    // Fabricated 6x4 trigger map: slot 4 with direction 3 (byte 0x64) at (2,1), (3,1) and (3,2),
    // slot 2 with direction 7 (byte 0xE2) at (0,3).
    private static readonly FieldZone Zone = BuildZone();

    [Fact]
    public void FindCellReturnsTheFirstSlotCellInRowMajorOrder()
    {
        Assert.Equal(new Vector2(2, 1), TriggerSlotLocator.FindCell(Zone, 4));
    }

    [Fact]
    public void FindDirectionReturnsTheHighThreeBits()
    {
        Assert.Equal(3, TriggerSlotLocator.FindDirection(Zone, 4));
        Assert.Equal(7, TriggerSlotLocator.FindDirection(Zone, 2));
    }

    [Fact]
    public void FindClimbTriggerReturnsCellAndDirection()
    {
        Assert.Equal((new Vector2(0, 3), 7), TriggerSlotLocator.FindClimbTrigger(Zone, 2));
    }

    [Fact]
    public void MissingSlotThrowsWithTheZoneName()
    {
        var error = Assert.Throws<InvalidOperationException>(() => TriggerSlotLocator.FindCell(Zone, 9));
        Assert.Contains("zone=T1", error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => TriggerSlotLocator.FindApproachCell(Zone, 9, Vector2.Zero));
        Assert.Throws<InvalidOperationException>(() => TriggerSlotLocator.FindClimbTrigger(Zone, 9));
    }

    [Fact]
    public void ApproachFromNorthEastTargetsBelowTheSouthernmostCell()
    {
        Assert.Equal(new Vector2(3, 3), TriggerSlotLocator.FindApproachCell(Zone, 4, new Vector2(5, 0)));
    }

    [Fact]
    public void ApproachFromEastTargetsRightOfTheEastColumnBottom()
    {
        Assert.Equal(new Vector2(4, 2), TriggerSlotLocator.FindApproachCell(Zone, 4, new Vector2(5, 2)));
    }

    [Fact]
    public void ApproachFromSouthWestStepsOutsideTheNearestCell()
    {
        Assert.Equal(new Vector2(1, 0), TriggerSlotLocator.FindApproachCell(Zone, 4, new Vector2(0, 3)));
    }

    private static FieldZone BuildZone()
    {
        byte[] cells =
        [
            0, 0, 0, 0, 0, 0,
            0, 0, 0x64, 0x64, 0, 0,
            0, 0, 0, 0x64, 0, 0,
            0xE2, 0, 0, 0, 0, 0,
        ];
        var zone = new Zone("T1", 6, 4, [], "floor", "trigger", [], [], "synthetic");
        byte[] floor = [.. Enumerable.Repeat((byte)1, 24)];
        return new FieldZone(zone, new FloorMap(6, 4, floor), new TriggerMap(6, 4, cells), []);
    }
}
