using Dw3Production.Conversion;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Counts the typed glyphs of a resolved battle text line, the unit the report typewriter reveals.</summary>
public sealed class BattleTextGlyphCountTests
{
    /// <summary>Glyphs and icons count, line breaks do not, and a substitution counts its own glyphs where it is inserted.</summary>
    [Fact]
    public void Count_IncludesSubstitutedGlyphsAndSkipsControls()
    {
        var line = new BattleTextLine(
            [Glyph(1), Glyph(2), Control(5, 1), Control(1), new TextToken("i", 3, 0, []), Glyph(4)],
            [[Glyph(7), Glyph(8), Glyph(9)]],
            "synthetic");

        Assert.Equal(7, BattleTextGlyphCount.Count(line));
    }

    /// <summary>An unresolved line types nothing.</summary>
    [Fact]
    public void Count_MissingLineIsZero() => Assert.Equal(0, BattleTextGlyphCount.Count(BattleTextLine.Missing));

    /// <summary>Builds a synthetic glyph token.</summary>
    /// <param name="code">Glyph code.</param>
    /// <returns>The glyph token.</returns>
    private static TextToken Glyph(int code) => new("g", code, 0, []);

    /// <summary>Builds a synthetic control token.</summary>
    /// <param name="command">Control command.</param>
    /// <param name="args">Control arguments.</param>
    /// <returns>The control token.</returns>
    private static TextToken Control(int command, params int[] args) => new("c", 0, command, args);
}
