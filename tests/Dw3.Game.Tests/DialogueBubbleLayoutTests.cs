using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;
using Xunit;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3.Game.Tests;

/// <summary>
/// Covers the pure talk-box placement and conversation-partner lookup on fabricated actors,
/// fonts and positions.
/// </summary>
public sealed class DialogueBubbleLayoutTests
{
    // With the camera at (160, 120) a world point maps to the same source screen point.
    private static readonly Vector2 Camera = new(160, 120);

    [Theory]
    [InlineData(0, 200, 150, 0)]
    [InlineData(0, 100, 150, 2)]
    [InlineData(4, 200, 150, 1)]
    [InlineData(4, 100, 150, 3)]
    [InlineData(0, 200, 100, 1)]
    [InlineData(0, 100, 100, 3)]
    [InlineData(4, 200, 200, 0)]
    [InlineData(4, 100, 200, 2)]
    public void TypeDependsOnFacingScreenHalfAndEdges(int direction, int x, int y, int expectedType)
    {
        var (_, type) = DialogueBubbleLayout.Place(Actor(x, y, direction), Camera);

        Assert.Equal(expectedType, type);
    }

    [Fact]
    public void OriginIsOffsetFromTheSpeakerByType()
    {
        Assert.Equal(new Vector2(200 + 0xB, 150 - 0x13), DialogueBubbleLayout.Place(Actor(200, 150, 0), Camera).BoxOrigin);
        Assert.Equal(new Vector2(100 - 0xB, 150 - 7), DialogueBubbleLayout.Place(Actor(100, 150, 4), Camera).BoxOrigin);
    }

    [Fact]
    public void WidthIsTheWidestLineClampedToTheBoxLimits()
    {
        var font = Font(advance: 10);
        var narrow = Enumerable.Repeat(Glyph(), 3).ToList();
        var twoLines = Enumerable.Repeat(Glyph(), 12).Append(Control(1)).Concat(Enumerable.Repeat(Glyph(), 2)).ToList();
        var wide = Enumerable.Repeat(Glyph(), 40).ToList();

        Assert.Equal(0x5F, DialogueBubbleLayout.MeasureWidth(narrow, font));
        Assert.Equal(120, DialogueBubbleLayout.MeasureWidth(twoLines, font));
        Assert.Equal(0x8B, DialogueBubbleLayout.MeasureWidth(wide, font));
    }

    [Fact]
    public void TheFacedTileFindsTheActorInsideItsBox()
    {
        var actor = Actor(100, 100, 0) with { Width = 16 };
        var zone = new ZoneDialogue(1, [Actor(0, 0, 0) with { Width = null }, actor]);
        var state = new GameState { Position = new NumericsVector2(84.7f, 100.2f) };
        state.SetFacing(6);

        var tile = DialogueActorLocator.FacingTile(state);

        Assert.Equal(new NumericsVector2(100, 100), tile);
        Assert.Same(actor, DialogueActorLocator.FindAt(zone, tile));
        Assert.Null(DialogueActorLocator.FindAt(zone, new NumericsVector2(100, 105)));
    }

    private static DialogueActor Actor(int x, int y, int direction) => new(1, x, y, direction, 16, []);

    private static TextToken Glyph() => new("g", 4, 0, []);

    private static TextToken Control(int command) => new("c", 0, command, []);

    private static FontData Font(int advance)
    {
        var metric = new GlyphMetric(0, 0, 0, 0, 0, 8, 8, 0, 0, advance);
        return new FontData(new Dictionary<int, GlyphMetric> { [4] = metric }, new Dictionary<int, GlyphMetric>(), "p0.png", "p1.png", 12, new Dictionary<int, FontStyleData>(), "font");
    }
}
