using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattlePrimitiveCompositorTests
{
    [Fact]
    public void OrdersFartherPrimitivesFirstAndReversesEqualKeys()
    {
        var order = BattlePrimitiveCompositor.Order(
        [
            new SourcePrimitive(5, 3),
            new SourcePrimitive(9, 3),
            new SourcePrimitive(5, 3),
            new SourcePrimitive(1, 3),
        ]);

        Assert.Equal([1, 2, 0, 3], order);
    }

    [Fact]
    public void RetainsAQuadAsOnePrimitiveUntilItsOrderedEmission()
    {
        var primitive = new SourcePrimitive(1024, 4);

        Assert.Equal(4, primitive.CornerCount);
        Assert.Equal([0, 1, 2, 2, 1, 3], BattlePrimitiveCompositor.EmitIndices(primitive));
    }

    [Fact]
    public void LeavesBackgroundForATransparentTexel()
    {
        var background = new Ps1CompositorColor(20, 40, 60);
        var transparent = new Ps1CompositorTexel(255, 255, 255, Transparent: true, Stp: false);

        Assert.Equal(background, BattlePrimitiveCompositor.Composite(background, transparent, Ps1AbrMode.Opaque));
    }

    [Fact]
    public void WritesOpaqueTexelsRegardlessOfTheStpFlag()
    {
        var opaqueStp = new Ps1CompositorTexel(90, 120, 150, Transparent: false, Stp: true);

        Assert.Equal(
            new Ps1CompositorColor(90, 120, 150),
            BattlePrimitiveCompositor.Composite(new Ps1CompositorColor(20, 40, 60), opaqueStp, Ps1AbrMode.Opaque));
    }

    [Fact]
    public void AbrZeroBlendsOnlyStpTexelsAtHalfBackgroundAndHalfForeground()
    {
        var background = new Ps1CompositorColor(20, 40, 60);
        var stp = new Ps1CompositorTexel(100, 160, 220, Transparent: false, Stp: true);
        var opaque = new Ps1CompositorTexel(100, 160, 220, Transparent: false, Stp: false);

        Assert.Equal(
            new Ps1CompositorColor(60, 100, 140),
            BattlePrimitiveCompositor.Composite(background, stp, Ps1AbrMode.HalfBackgroundHalfForeground));
        Assert.Equal(
            new Ps1CompositorColor(100, 160, 220),
            BattlePrimitiveCompositor.Composite(background, opaque, Ps1AbrMode.HalfBackgroundHalfForeground));
    }

    [Fact]
    public void AbrOneAddsOnlyStpTexelsAndSaturates()
    {
        var background = new Ps1CompositorColor(200, 100, 20);
        var stp = new Ps1CompositorTexel(100, 180, 250, Transparent: false, Stp: true);
        var opaque = new Ps1CompositorTexel(100, 180, 250, Transparent: false, Stp: false);

        Assert.Equal(
            new Ps1CompositorColor(255, 255, 255),
            BattlePrimitiveCompositor.Composite(background, stp, Ps1AbrMode.Additive));
        Assert.Equal(
            new Ps1CompositorColor(100, 180, 250),
            BattlePrimitiveCompositor.Composite(background, opaque, Ps1AbrMode.Additive));
    }
}
