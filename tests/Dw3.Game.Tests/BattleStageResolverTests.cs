using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleStageResolverTests
{
    [Fact]
    public void ResolvesTheStageFromNestedEncounterTables()
    {
        var battle = new EncounterAreaBattle(Encounter: 37, Stage: 12, Music: 0, MusicName: string.Empty);
        var area = new EncounterBattleList(0, [battle]);
        var place = new EncounterFieldBattles(0, 0, 0, [area]);
        var array = new EncounterStageArray(0, [place]);
        var stage = new EncounterStage(string.Empty, string.Empty, [array]);
        var data = new EncounterData([], [], [], new Dictionary<string, EncounterStage> { ["S001"] = stage });

        Assert.Equal(12, BattleStageResolver.Resolve(data, 37));
    }

    [Fact]
    public void UsesTheDebugStageWhenEncounterIsMissing()
    {
        var data = new EncounterData([], [], [], new Dictionary<string, EncounterStage>());

        Assert.Equal(BattleStageResolver.DebugStage, BattleStageResolver.Resolve(data, 999));
        Assert.Equal(BattleStageResolver.DebugStage, BattleStageResolver.Resolve(null, 999));
    }
}
