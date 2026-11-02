using Dw3Production.Diagnostics.Field;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class FieldProbeCatalogTests
{
    [Theory]
    [InlineData("RunLocomotionProbe", typeof(LocomotionProbe))]
    [InlineData("RunF3Probe", typeof(F3Probe))]
    [InlineData("RunF3PerformanceProbe", typeof(F3PerformanceProbe))]
    [InlineData("RunDepthProbe", typeof(DepthProbe))]
    [InlineData("RunWaterProbe", typeof(WaterProbe))]
    [InlineData("RunSortYProbe", typeof(SortYProbe))]
    [InlineData("RunCueProbe", typeof(CueProbe))]
    [InlineData("RunM1AcceptanceTest", typeof(M1AcceptanceProbe))]
    [InlineData("RunM2NavProbe", typeof(M2NavProbe))]
    [InlineData("RunConditionalProbe", typeof(ConditionalProbe))]
    [InlineData("RunGatedBridgeProbe", typeof(GatedBridgeProbe))]
    [InlineData("RunNpcProbe", typeof(NpcProbe))]
    [InlineData("RunM2VideoProbe", typeof(M2VideoProbe))]
    [InlineData("RunObjectProbe", typeof(ObjectProbe))]
    [InlineData("RunClimbProbe", typeof(ClimbProbe))]
    [InlineData("RunDropProbe", typeof(DropProbe))]
    [InlineData("RunWarpProbe", typeof(WarpProbe))]
    [InlineData("RunCanalWaterProbe", typeof(CanalWaterProbe))]
    [InlineData("RunIconProbe", typeof(IconProbe))]
    public void FieldEntryNamesCreateTheirProbe(string entryName, Type expected)
    {
        var probe = FieldProbeCatalog.Create(entryName, null!);

        Assert.IsType(expected, probe);
    }

    [Theory]
    [InlineData("RunDialogueProbe")]
    [InlineData("RunBattlePlayProbe")]
    [InlineData("RunSqliteProbe")]
    public void OtherFamiliesAreNotFieldProbes(string entryName)
    {
        Assert.Null(FieldProbeCatalog.Create(entryName, null!));
    }

    [Fact]
    public void TheDialogueEntryNameCreatesTheDialogueProbe()
    {
        Assert.IsType<DialogueProbe>(DialogueProbeCatalog.Create("RunDialogueProbe", null!));
        Assert.Null(DialogueProbeCatalog.Create("RunIconProbe", null!));
    }
}
