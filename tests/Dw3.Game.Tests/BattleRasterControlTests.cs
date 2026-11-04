using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleRasterControlTests
{
    [Fact]
    public void DefaultControlsDoNotInterveneInTheProductionStream()
    {
        var control = new BattleRasterControl();
        var primitive = Primitive(101, 2);
        Assert.True(control.Includes(primitive));
        Assert.False(control.UsesWrongBlend(primitive));
        Assert.Null(control.Fixture);
        Assert.False(control.ClearIndices);
        Assert.Equal(int.MaxValue, control.Limit);
    }

    [Fact]
    public void HiddenFighterKeepsTheOpponentAndOtherBonesUntouched()
    {
        var control = new BattleRasterControl { HiddenFighter = 101 };
        Assert.False(control.Includes(Primitive(101, 2)));
        Assert.True(control.Includes(Primitive(102, 2)));
        control.HiddenFighter = -1;
        control.HiddenBone = 2;
        Assert.False(control.Includes(Primitive(101, 2)));
        Assert.True(control.Includes(Primitive(101, 3)));
    }

    [Fact]
    public void WrongBlendChangesOnlyTheExactLastPrimitiveOfThePrefix()
    {
        var first = Primitive(101, 2);
        var selected = Primitive(101, 2);
        var control = new BattleRasterControl { Ordered = [first, selected], Limit = 2, WrongBlend = true };
        Assert.False(control.UsesWrongBlend(first));
        Assert.True(control.UsesWrongBlend(selected));
        control.WrongBlend = false;
        Assert.False(control.UsesWrongBlend(selected));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public void OutOfRangeCutoffNeverSelectsAnUnrelatedPrimitive(int limit)
    {
        var primitive = Primitive(101, 2);
        var control = new BattleRasterControl { Ordered = [primitive], Limit = limit, WrongBlend = true };
        Assert.False(control.UsesWrongBlend(primitive));
    }

    private static BattleRasterPrimitive Primitive(int fighter, int bone)
    {
        // Fresh arrays distinguish source commands with identical numeric metadata.
        var polygon = new BattleModel.SourcePolygon(bone, 0, 3, 0, 1, new Godot.Vector3[3], new Godot.Vector2[3], new Godot.Color[3]);
        return new BattleRasterPrimitive(0, polygon, new Godot.Vector2[3], null!, fighter);
    }
}
