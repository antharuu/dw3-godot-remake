using Dw3Production.Diagnostics.Battle;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleProbeCatalogTests
{
    [Theory]
    [InlineData("RunBattleCameraProbe", typeof(BattleCameraProbe))]
    [InlineData("RunBattleMotionProbe", typeof(BattleMotionProbe))]
    [InlineData("RunBattleFontProbe", typeof(BattleFontProbe))]
    [InlineData("RunBattleEntranceProbe", typeof(BattleEntranceProbe))]
    [InlineData("RunBattleFaceProbe", typeof(BattleFaceProbe))]
    [InlineData("RunBattlePlayProbe", typeof(BattlePlayProbe))]
    [InlineData("RunBattleReportProbe", typeof(BattleReportProbe))]
    public void BattleEntryNamesCreateTheirProbe(string entryName, Type expected)
    {
        var probe = BattleProbeCatalog.Create(entryName, null!);

        Assert.IsType(expected, probe);
    }

    [Theory]
    [InlineData("RunDialogueProbe")]
    [InlineData("RunWarpProbe")]
    [InlineData("RunUnknownProbe")]
    public void OtherEntryNamesAreLeftToTheirOwner(string entryName)
    {
        Assert.Null(BattleProbeCatalog.Create(entryName, null!));
    }
}
