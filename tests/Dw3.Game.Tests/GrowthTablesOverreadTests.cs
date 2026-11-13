using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Covers the unbounded growth-table reads of the original raise: a stat or resistance growth of 5 plus the random
/// offset reaches past its row or table, which the original reads from the adjacent memory instead of failing.
/// </summary>
public sealed class GrowthTablesOverreadTests
{
    [Fact]
    public void StatGainPastARowContinuesIntoTheNextRow()
    {
        var tables = Synthetic();

        Assert.Equal(108, tables.StatGain(0, 8));
        Assert.Equal(200, tables.StatGain(0, 9));
        Assert.Equal(300, tables.StatGain(1, 9));
    }

    [Fact]
    public void StatGainPastTheLastRowContinuesIntoTheResistanceGains()
    {
        var tables = Synthetic();

        Assert.Equal(608, tables.StatGain(5, 8));
        Assert.Equal(1, tables.StatGain(5, 9));
    }

    [Fact]
    public void ResistGainPastTheTableReadsZero()
    {
        var tables = Synthetic();

        Assert.Equal(8, tables.ResistGain(7));
        Assert.Equal(0, tables.ResistGain(8));
        Assert.Equal(0, tables.ResistGain(11));
    }

    [Fact]
    public void RaiseWithGrowthFiveAndTheHighestRandomDoesNotFail()
    {
        var tables = Synthetic();
        var state = new PartnerGrowthState { Level = 2 };
        var curve = new PartnerGrowthCurve(10, 0, 0, [5, 5, 5, 5, 5, 5], [5, 5, 5, 5, 5, 5, 5]);

        GrowthCalculator.RaiseStats(tables, state, curve, 2, new FixedRng(4099));

        Assert.All(state.BattleStats, value => Assert.Equal(200, value));
        Assert.All(state.Resistances, value => Assert.Equal(0, value));
    }

    private static GrowthTables Synthetic()
    {
        var tables = new GrowthTables { ResistGains = [1, 2, 3, 4, 5, 6, 7, 8] };
        for (var row = 0; row < tables.StatGains.Length; row++)
        {
            for (var column = 0; column < 9; column++)
            {
                tables.StatGains[row][column] = ((row + 1) * 100) + column;
            }
        }

        return tables;
    }

    private sealed class FixedRng(int value) : IBattleRng
    {
        public int NextValue() => value;
    }
}
