using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies that report rendering has decoded names for the partner and all three equipped form slots.</summary>
public sealed class FightReportNameCatalogTests
{
    /// <summary>Build includes form slot ids without replacing the partner's own display identity.</summary>
    [Fact]
    public void Build_MapsPartnerAndEquippedFormSlots()
    {
        var input = new FightReportInput(
            [new FightReportPartner(0, 0, 1, 1, 0, 0, 1, [5, 6, 7])],
            Money: 0,
            Item: 0);

        Dictionary<int, BattleTextLine> names = FightReportNameCatalog.Build(input, Name);

        Assert.Equal([1, 5, 6, 7], names.Keys.Order());
        Assert.All(names, pair => Assert.Equal($"name={pair.Key}", pair.Value.Debug));
    }

    /// <summary>Builds a fabricated decoded name line for one fighter or form id.</summary>
    /// <param name="id">Fighter or form identifier.</param>
    /// <returns>A distinct renderable line.</returns>
    private static BattleTextLine Name(int id) => BattleTextCatalog.Tokens([], $"name={id}");
}
