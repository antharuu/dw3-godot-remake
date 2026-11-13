using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The widescreen coverage policy of sprite effects. Sprites keep their source position and size and stay
/// clipped to the 4:3 source screen centred in the 16:9 viewport, like the console's screen edge. A frame
/// that covers the whole source screen width from a screen anchor is a full-screen backdrop: it is widened
/// about the screen centre to the viewport width, and while it shows every sprite is clipped to the viewport
/// instead of the source screen, keeping its source position and size, so the stage is not
/// exposed in the lateral margins. This is a deliberate remake policy for the 16:9 default, not a console behaviour.
/// </summary>
public static class BattleSpriteCoverage
{
    /// <summary>Gets the horizontal factor that stretches the source screen width to the viewport width.</summary>
    public static float WideFactor => BattleHudLayout.ViewportWidth / (float)(BattleHudLayout.SourceWidth * BattleHudLayout.Scale);

    /// <summary>Gets the source screen rectangle in viewport pixels.</summary>
    public static Rect2 SourceClip => new(BattleHudLayout.OffsetX, BattleHudLayout.OffsetY, BattleHudLayout.SourceWidth * BattleHudLayout.Scale, BattleHudLayout.SourceHeight * BattleHudLayout.Scale);

    /// <summary>Gets the whole viewport rectangle.</summary>
    public static Rect2 ViewportClip => new(0, 0, BattleHudLayout.ViewportWidth, BattleHudLayout.ViewportHeight);

    /// <summary>Gets the transform widening a full-screen backdrop about the viewport's vertical centre line.</summary>
    public static Transform2D Widen
    {
        get
        {
            float factor = WideFactor;
            float centre = BattleHudLayout.ViewportWidth / 2f;
            return new Transform2D(new Vector2(factor, 0), new Vector2(0, 1), new Vector2(centre * (1 - factor), 0));
        }
    }

    /// <summary>Converts a viewport x coordinate to a source screen x coordinate.</summary>
    /// <param name="viewportX">Viewport x in pixels.</param>
    /// <returns>Source screen x.</returns>
    public static float ToSourceX(float viewportX) => (viewportX - BattleHudLayout.OffsetX) / BattleHudLayout.Scale;

    /// <summary>
    /// Tells whether an unscaled frame placed at a source pivot spans the whole source screen width, the
    /// geometry of a full-screen backdrop. Scale and rotation are ignored so a backdrop growing from zero
    /// is treated the same way during its whole animation.
    /// </summary>
    /// <param name="frame">Decoded frame.</param>
    /// <param name="pivotSourceX">Pivot x in source screen pixels.</param>
    /// <returns>Whether the frame covers x from 0 to the source width.</returns>
    public static bool SpansSourceScreen(BattleSpriteFrame frame, float pivotSourceX)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Parts.Count == 0)
        {
            return false;
        }

        float left = pivotSourceX + frame.Parts.Min(part => part.Dx);
        float right = pivotSourceX + frame.Parts.Max(part => part.Dx + part.Width);
        return left <= 0 && right >= BattleHudLayout.SourceWidth;
    }
}
