using Dw3Production.Session;
using Dw3Production.Simulation;

namespace Dw3.Game.Tests.Session;

/// <summary>Commits a resolved victory the way a caller that does not play the report does.</summary>
internal static class ResolutionSettlement
{
    /// <summary>Commits every report stage of the resolution's settlement and returns the settled report rows.</summary>
    /// <param name="resolution">Resolution returned at the battle's end.</param>
    /// <returns>The resolution whose report input carries the settled rows.</returns>
    public static BattleOutcomeResolution CommitAll(this BattleOutcomeResolution resolution)
    {
        VictorySettlement settlement = Xunit.Assert.IsType<VictorySettlement>(resolution.Settlement);
        settlement.CommitAll();
        return resolution with { FightReport = settlement.Settled };
    }
}
