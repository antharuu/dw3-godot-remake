using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies that normal Digivolution eligibility consumes owned forms and source conditions.</summary>
public sealed class DigivolutionEligibilityTests
{
    /// <summary>Rejects an owned form when its source stat condition is unmet while retaining an eligible owned form.</summary>
    [Fact]
    public void AvailableForms_UsesKnownFormsAndSourceRequirements()
    {
        var stats = new BattleStats { Level = 10 };
        stats.Stats[BattleStat.Attack] = 30;
        var partner = new PartnerDigivolution(
            Partner: 0,
            BaseId: 1,
            Evolutions:
            [
                new DigivolutionEntry(1, 2, [new EvolutionNeed(0, 1, 10)], Stat: 7, Value: 10),
                new DigivolutionEntry(2, 3, [], Stat: 1, Value: 31),
                new DigivolutionEntry(3, 4, [new EvolutionNeed(1, 2, 11)], Stat: 0, Value: 0),
                new DigivolutionEntry(4, 5, [], Stat: 7, Value: 1),
            ]);

        IReadOnlyList<int> forms = DigivolutionEligibility.AvailableForms(partner, [1, 2, 3, 4], stats);

        Assert.Equal([2], forms);
    }
}
