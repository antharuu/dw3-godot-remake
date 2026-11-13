using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Godot;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleSpriteCompositionTests
{
    [Fact]
    public void SpriteAnchorEntryIsAQuarterOfAPolygonEntryAtTheSameDepth()
    {
        Assert.Equal(6000 / 64, BattleSpriteOrdering.PointEntry(6000));

        // The triangle average uses the 0x555 fixed-point third, which rounds 3 * 375 down to 374.
        Assert.Equal(374, BattleSpriteOrdering.PolygonEntry([6000, 6000, 6000]));
        Assert.Equal(6000 / 16, BattleSpriteOrdering.PolygonEntry([6000, 6000, 6000, 6000]));
        Assert.Throws<ArgumentOutOfRangeException>(() => BattleSpriteOrdering.PolygonEntry([1, 2]));
    }

    [Fact]
    public void SpriteFarBehindAModelIsDrawnBeforeItsPolygons()
    {
        int[] polygons = [BattleSpriteOrdering.PolygonEntry([6200, 6100, 6000]), BattleSpriteOrdering.PolygonEntry([6000, 5900, 5800])];

        Assert.Equal(0, BattleSpriteOrdering.InsertionPoint(polygons, BattleSpriteOrdering.PointEntry(30000)));
    }

    [Fact]
    public void SpriteAtTheModelDepthIsDrawnAfterItsPolygons()
    {
        int[] polygons = [BattleSpriteOrdering.PolygonEntry([6200, 6100, 6000]), BattleSpriteOrdering.PolygonEntry([6000, 5900, 5800])];

        Assert.Equal(polygons.Length, BattleSpriteOrdering.InsertionPoint(polygons, BattleSpriteOrdering.PointEntry(6000)));
    }

    [Fact]
    public void SpriteSharingAnEntryIsDrawnBeforeThePolygonsOfThatEntry()
    {
        int[] polygons = [400, 300, 300, 200];

        Assert.Equal(1, BattleSpriteOrdering.InsertionPoint(polygons, 300));
        Assert.Equal(0, BattleSpriteOrdering.InsertionPoint(polygons, BattleSpriteOrdering.BackEntry));
        Assert.Equal(4, BattleSpriteOrdering.InsertionPoint(polygons, BattleSpriteOrdering.FrontEntry));
    }

    [Fact]
    public void FullWidthScreenFrameIsABackdropAndASmallFrameIsNot()
    {
        var band = Frame([.. Enumerable.Range(0, 10).Select(i => new BattleSpritePart("band", i * 32, -40, 32, 40, "effect-page"))]);
        var spark = Frame([new BattleSpritePart("spark", -28, -28, 56, 56, "effect-page")]);

        Assert.True(BattleSpriteCoverage.SpansSourceScreen(band, 0));
        Assert.False(BattleSpriteCoverage.SpansSourceScreen(band, 1));
        Assert.False(BattleSpriteCoverage.SpansSourceScreen(spark, 160));
        Assert.False(BattleSpriteCoverage.SpansSourceScreen(Frame([]), 0));
    }

    [Fact]
    public void WidenMapsTheSourceScreenEdgesToTheViewportEdges()
    {
        Transform2D widen = BattleSpriteCoverage.Widen;

        Assert.Equal(0f, (widen * new Vector2(BattleHudLayout.OffsetX, 10)).X, 3);
        Assert.Equal(BattleHudLayout.ViewportWidth, (widen * new Vector2(BattleHudLayout.ViewportWidth - BattleHudLayout.OffsetX, 10)).X, 3);
        Assert.Equal(10f, (widen * new Vector2(BattleHudLayout.OffsetX, 10)).Y, 3);
        Assert.Equal(0f, BattleSpriteCoverage.ToSourceX(BattleHudLayout.OffsetX), 3);
        Assert.Equal(new Rect2(0, 0, 1280, 720), BattleSpriteCoverage.ViewportClip);
        Assert.Equal(new Rect2(160, 0, 960, 720), BattleSpriteCoverage.SourceClip);
    }

    private static BattleSpriteFrame Frame(BattleSpritePart[] parts) => new(38, 0, -1, parts);
}
