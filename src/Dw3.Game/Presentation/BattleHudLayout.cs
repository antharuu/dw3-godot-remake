namespace Dw3Production.Presentation;

/// <summary>One PS1 320×240 screen mapped pixel-exactly into the 1280×720 16:9 battle viewport.
/// The vertical fit is fixed at 3×, leaving 160 viewport pixels on each side.</summary>
public static class BattleHudLayout
{
    public const int SourceWidth = 320;
    public const int SourceHeight = 240;
    public const int ViewportWidth = 1280;
    public const int ViewportHeight = 720;
    public const int Scale = 3;
    public const int OffsetX = (ViewportWidth - SourceWidth * Scale) / 2;
    public const int OffsetY = (ViewportHeight - SourceHeight * Scale) / 2;

    public static (int X, int Y) ToViewport(int sourceX, int sourceY) =>
        (sourceX * Scale + OffsetX, sourceY * Scale + OffsetY);

    public static (int Width, int Height) ToViewportSize(int sourceWidth, int sourceHeight) =>
        (sourceWidth * Scale, sourceHeight * Scale);

    public static bool FitsSourceRect(int x, int y, int width, int height) =>
        x >= 0 && y >= 0 && width >= 0 && height >= 0 && x + width <= SourceWidth && y + height <= SourceHeight;
}
