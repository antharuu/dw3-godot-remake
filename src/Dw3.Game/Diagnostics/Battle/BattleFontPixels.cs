using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Matches framebuffer texels against exact decoded battle-font CLUT colors.</summary>
internal static class BattleFontPixels
{
    /// <summary>Collects every opaque RGB value from both pages of one decoded palette.</summary>
    /// <param name="font">Decoded font manifest.</param>
    /// <param name="style">One-based source text style.</param>
    /// <param name="palette">Source CLUT row.</param>
    /// <returns>The exact RGB values used by that palette.</returns>
    public static HashSet<(byte R, byte G, byte B)> Palette(FontData font, int style, int palette)
    {
        var colors = new HashSet<(byte R, byte G, byte B)>();
        for (var page = 0; page < 2; page++)
        {
            var image = Image.LoadFromFile(font.PalettePagePath(style, palette, page));
            CollectOpaqueColors(image, colors);
        }

        return colors;
    }

    /// <summary>Counts pixels in a clipped viewport rectangle that exactly match a CLUT color.</summary>
    /// <param name="capture">Framebuffer capture.</param>
    /// <param name="rectangle">Viewport rectangle, in pixels.</param>
    /// <param name="colors">Allowed exact RGB colors.</param>
    /// <returns>The matching pixel count.</returns>
    public static int Count(Image capture, Rect2 rectangle, HashSet<(byte R, byte G, byte B)> colors)
    {
        var count = 0;
        var fromX = Math.Max(0, (int)rectangle.Position.X);
        var toX = Math.Min(capture.GetWidth(), (int)rectangle.End.X);
        var fromY = Math.Max(0, (int)rectangle.Position.Y);
        var toY = Math.Min(capture.GetHeight(), (int)rectangle.End.Y);
        for (var y = fromY; y < toY; y++)
        {
            for (var x = fromX; x < toX; x++)
            {
                var pixel = capture.GetPixel(x, y);
                if (colors.Contains(((byte)pixel.R8, (byte)pixel.G8, (byte)pixel.B8)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Adds opaque RGB values from one decoded atlas page.</summary>
    /// <param name="image">One decoded palette page.</param>
    /// <param name="colors">Set receiving unique RGB values.</param>
    private static void CollectOpaqueColors(Image image, HashSet<(byte R, byte G, byte B)> colors)
    {
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.A8 != 0)
                {
                    colors.Add(((byte)pixel.R8, (byte)pixel.G8, (byte)pixel.B8));
                }
            }
        }
    }
}
