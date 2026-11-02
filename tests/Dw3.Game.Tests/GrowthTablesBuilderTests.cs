using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Covers the copy of the decoded growth tables into the fixed-size runtime tables: short
/// sources leave the remaining slots at zero and long sources are truncated.
/// </summary>
public sealed class GrowthTablesBuilderTests
{
    [Fact]
    public void ValuesAreCopiedSlotBySlot()
    {
        var data = new GrowthTablesData(
            [10, 20, 30, 40],
            [1, 2, 3, 4],
            [5, 6, 7, 8, 9, 10, 11, 12, 13],
            [[1, 2, 3], [4, 5]],
            [7, 7, 7, 7, 7, 7, 7, 7]);

        var tables = GrowthTablesBuilder.Build(data);

        Assert.Equal([10, 20, 30, 40], tables.LevelExp);
        Assert.Equal([1, 2, 3, 4], tables.VitalCuts);
        Assert.Equal([5, 6, 7, 8, 9, 10, 11, 12, 13], tables.VitalRandom);
        Assert.Equal([1, 2, 3, 0, 0, 0, 0, 0, 0], tables.StatGains[0]);
        Assert.Equal([4, 5, 0, 0, 0, 0, 0, 0, 0], tables.StatGains[1]);
        Assert.All(tables.StatGains.Skip(2), row => Assert.All(row, value => Assert.Equal(0, value)));
        Assert.Equal([7, 7, 7, 7, 7, 7, 7, 7], tables.ResistGains);
    }

    [Fact]
    public void LongerSourcesAreTruncatedToTheTableSize()
    {
        var data = new GrowthTablesData([1, 2, 3, 4, 5, 6], [], [], [], []);

        var tables = GrowthTablesBuilder.Build(data);

        Assert.Equal([1, 2, 3, 4], tables.LevelExp);
        Assert.All(tables.VitalCuts, value => Assert.Equal(0, value));
    }
}
