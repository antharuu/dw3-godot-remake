namespace Dw3Production.Presentation;

/// <summary>One PS1 320×240 screen mapped pixel-exactly into the 1280×720 16:9 battle viewport.
/// The vertical fit is fixed at 3×, leaving 160 viewport pixels on each side.</summary>
public static class BattleHudLayout
{
    /// <summary>Width of the authored battle HUD coordinate space, in source pixels.</summary>
    public const int SourceWidth = 320;

    /// <summary>Height of the authored battle HUD coordinate space, in source pixels.</summary>
    public const int SourceHeight = 240;

    /// <summary>Width of the widescreen battle viewport, in physical display pixels.</summary>
    public const int ViewportWidth = 1280;

    /// <summary>Height of the widescreen battle viewport, in physical display pixels.</summary>
    public const int ViewportHeight = 720;

    /// <summary>Integer multiplier that preserves every source pixel edge in the viewport.</summary>
    public const int Scale = 3;

    /// <summary>Left inset of the centered source rectangle, in viewport pixels.</summary>
    public const int OffsetX = (ViewportWidth - SourceWidth * Scale) / 2;

    /// <summary>Top inset of the centered source rectangle, in viewport pixels.</summary>
    public const int OffsetY = (ViewportHeight - SourceHeight * Scale) / 2;

    /// <summary>Maps an authored HUD position into the centered, integer-scaled battle viewport.</summary>
    /// <param name="sourceX">Horizontal source coordinate, in source pixels.</param>
    /// <param name="sourceY">Vertical source coordinate, in source pixels.</param>
    /// <returns>The physical viewport coordinate occupied by the source pixel.</returns>
    public static (int X, int Y) ToViewport(int sourceX, int sourceY) =>
        (sourceX * Scale + OffsetX, sourceY * Scale + OffsetY);

    /// <summary>Expands an authored HUD size without changing its source-pixel aspect ratio.</summary>
    /// <param name="sourceWidth">Width in source pixels.</param>
    /// <param name="sourceHeight">Height in source pixels.</param>
    /// <returns>The corresponding width and height in physical viewport pixels.</returns>
    public static (int Width, int Height) ToViewportSize(int sourceWidth, int sourceHeight) =>
        (sourceWidth * Scale, sourceHeight * Scale);

    /// <summary>Checks whether a non-negative source rectangle remains inside the 320 by 240 HUD canvas.</summary>
    /// <param name="x">Left edge in source pixels.</param>
    /// <param name="y">Top edge in source pixels.</param>
    /// <param name="width">Rectangle width in source pixels.</param>
    /// <param name="height">Rectangle height in source pixels.</param>
    /// <returns><see langword="true"/> only when every rectangle edge lies within the source canvas.</returns>
    public static bool FitsSourceRect(int x, int y, int width, int height) =>
        x >= 0 && y >= 0 && width >= 0 && height >= 0 && x + width <= SourceWidth && y + height <= SourceHeight;
}
