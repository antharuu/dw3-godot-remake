using System.Numerics;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class FieldCollisionRecoveryTests
{
    [Fact]
    public void CircleRejectsVoidWithinThePlayerRadius()
    {
        byte[] cells = [.. Enumerable.Repeat((byte)1, 40 * 40)];
        cells[(16 * 40) + 24] = 0;
        var floor = new FloorMap(40, 40, cells);

        Assert.False(FieldCollisionRecovery.CircleFreeAt(floor, new Vector2(16.5f, 16.5f)));
        Assert.True(FieldCollisionRecovery.CircleFreeAt(floor, new Vector2(12.5f, 28.5f)));
    }

    [Fact]
    public void DepenetrationReturnsNearestFreeCandidateInSourceOrder()
    {
        byte[] cells = [.. Enumerable.Repeat((byte)1, 40 * 40)];
        cells[(16 * 40) + 16] = 0;
        var floor = new FloorMap(40, 40, cells);

        Vector2 recovered = FieldCollisionRecovery.Depenetrate(floor, new Vector2(16.5f, 16.5f));

        Assert.Equal(new Vector2(23.5f, 23.5f), recovered);
    }
}
