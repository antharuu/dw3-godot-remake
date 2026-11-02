using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Measures where a sheet fighter differs from its empty-stage background.</summary>
internal static class FighterSheetBounds
{
    /// <summary>Summed RGB difference, in 0-255 units, at or below which a pixel is background.</summary>
    private const float BackgroundTolerance = 45f;

    /// <summary>Counts fighter pixels and reports whether any touches the image border.</summary>
    /// <param name="image">Frame with the fighter.</param>
    /// <param name="background">Same framing without the fighter.</param>
    /// <returns>The textured pixel count and whether the fighter touches the frame.</returns>
    /// <exception cref="InvalidOperationException">The images have different dimensions.</exception>
    public static (int TexturedPixels, bool TouchesFrame) Measure(Image image, Image background)
    {
        if (image.GetWidth() != background.GetWidth() || image.GetHeight() != background.GetHeight())
            throw new InvalidOperationException("sheet capture/background dimensions differ");
        var count = 0;
        var touches = false;
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                if (!IsFighterPixel(image.GetPixel(x, y), background.GetPixel(x, y))) continue;
                count++;
                touches |= IsBorder(image, x, y);
            }
        }

        return (count, touches);
    }

    /// <summary>Returns whether a pixel differs visibly from the background.</summary>
    /// <param name="a">Frame pixel.</param>
    /// <param name="b">Background pixel.</param>
    /// <returns>True when the summed channel difference exceeds the tolerance.</returns>
    private static bool IsFighterPixel(Color a, Color b) =>
        (Mathf.Abs(a.R - b.R) * 255f) + (Mathf.Abs(a.G - b.G) * 255f) + (Mathf.Abs(a.B - b.B) * 255f) > BackgroundTolerance;

    /// <summary>Returns whether a pixel lies on the image border.</summary>
    /// <param name="image">Measured image.</param>
    /// <param name="x">Pixel column.</param>
    /// <param name="y">Pixel row.</param>
    /// <returns>True on the first or last row or column.</returns>
    private static bool IsBorder(Image image, int x, int y) =>
        x == 0 || y == 0 || x == image.GetWidth() - 1 || y == image.GetHeight() - 1;
}
