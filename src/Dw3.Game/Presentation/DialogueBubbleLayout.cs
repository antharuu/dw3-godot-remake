using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Places the talk box of a field conversation the way the game does, in source screen pixels
/// (320x240). The box sits beside the speaker on the side away from the nearest screen edge, and
/// flips above or below the speaker near the top or bottom of the screen. Its width is fixed for
/// the whole conversation by the widest line of the full text.
/// </summary>
internal static class DialogueBubbleLayout
{
    /// <summary>Source screen column from which a speaker counts as being on the right half (160).</summary>
    private const int RightHalfX = 0xA0;

    /// <summary>Source screen row above which a box drawn above the speaker would leave the screen, so it flips below.</summary>
    private const int TopFlipY = 0x79;

    /// <summary>Source screen row from which a box drawn below the speaker would leave the screen, so it flips above.</summary>
    private const int BottomFlipY = 0xAC;

    /// <summary>Narrowest talk box, in source pixels.</summary>
    private const int MinWidth = 0x5F;

    /// <summary>Widest talk box, in source pixels.</summary>
    private const int MaxWidth = 0x8B;

    /// <summary>
    /// Returns the talk box origin and type for a speaker. Types 0 and 1 extend the box to the left
    /// of its origin, 2 and 3 to the right; types 0 and 2 draw it above the speaker, 1 and 3 below.
    /// </summary>
    /// <param name="actor">Speaker, in world units, with its facing.</param>
    /// <param name="camera">Field camera centre in world units.</param>
    /// <returns>The box origin in source screen pixels and the box type (0..3).</returns>
    public static (Vector2 BoxOrigin, int Type) Place(DialogueActor actor, Vector2 camera)
    {
        // The scroll is the camera centre minus half of the 320x240 source screen.
        var srcX = actor.X - camera.X + 160f;
        var srcY = actor.Y - camera.Y + 120f;
        var type = FlipNearEdge(SideType(actor.Dir, srcX), srcY);
        var boxX = srcX + (type is 2 or 3 ? -0xB : 0xB);
        var boxY = srcY + (type is 0 or 2 ? -0x13 : -7);
        return (new Vector2(boxX, boxY), type);
    }

    /// <summary>
    /// Measures the box width: the widest line of the full text, lines being broken only by the
    /// explicit newline control (command 1), clamped to 0x5F..0x8B source pixels.
    /// </summary>
    /// <param name="tokens">Full text of the conversation, every page included.</param>
    /// <param name="font">Dialogue font; tokens without a glyph or icon metric add no width.</param>
    /// <returns>The box width in source pixels.</returns>
    public static int MeasureWidth(IReadOnlyList<TextToken> tokens, FontData font)
    {
        var maxWidth = 0;
        var cursor = 0;
        foreach (var token in tokens)
        {
            if (token.IsControl)
            {
                cursor = token.Command == 1 ? 0 : cursor;
                continue;
            }

            if (MetricOf(token, font) is { } metric)
            {
                cursor += metric.Advance + metric.Dx;
                maxWidth = Math.Max(maxWidth, cursor);
            }
        }

        return Math.Clamp(maxWidth, MinWidth, MaxWidth);
    }

    /// <summary>Returns the metric of a glyph or icon token.</summary>
    /// <param name="token">Non-control token.</param>
    /// <param name="font">Dialogue font.</param>
    /// <returns>The metric, or <see langword="null"/> when the font has none for the code.</returns>
    private static GlyphMetric? MetricOf(TextToken token, FontData font)
    {
        var table = token.IsGlyph ? font.Glyphs : font.Icons;
        return table.TryGetValue(token.Code, out var metric) ? metric : null;
    }

    /// <summary>
    /// Chooses the vertical side of the box from the speaker facing: directions 3, 4 and 5 put it
    /// below the speaker (types 1 and 3), the others above (types 0 and 2). Within each pair, a
    /// speaker on the right half of the screen gets the box that extends to its left.
    /// </summary>
    /// <param name="direction">Speaker facing, 0..7.</param>
    /// <param name="srcX">Speaker column in source screen pixels.</param>
    /// <returns>The initial box type.</returns>
    private static int SideType(int direction, float srcX)
    {
        var below = direction is 3 or 4 or 5;
        var type = below ? 1 : 0;
        return srcX >= RightHalfX ? type : type + 2;
    }

    /// <summary>Flips the box to the other vertical side when it would leave the screen.</summary>
    /// <param name="type">Initial box type.</param>
    /// <param name="srcY">Speaker row in source screen pixels.</param>
    /// <returns>The final box type.</returns>
    private static int FlipNearEdge(int type, float srcY) => type switch
    {
        0 when srcY < TopFlipY => 1,
        2 when srcY < TopFlipY => 3,
        1 when srcY >= BottomFlipY => 0,
        3 when srcY >= BottomFlipY => 2,
        _ => type,
    };
}
