using Dw3Production.Diagnostics.Field;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Measures battle framebuffer regions without changing the images under test.</summary>
internal static class BattlePixelMetrics
{
    /// <summary>Counts pixels whose RGB delta exceeds the probe threshold in a horizontal span.</summary>
    /// <param name="first">First framebuffer.</param>
    /// <param name="second">Second framebuffer.</param>
    /// <param name="xMin">Inclusive left pixel.</param>
    /// <param name="xMax">Exclusive right pixel.</param>
    /// <returns>The changed-pixel count.</returns>
    public static int CountDifferent(Image first, Image second, int xMin, int xMax)
    {
        var firstData = first.GetData();
        var secondData = second.GetData();
        var bytesPerPixel = first.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        var count = 0;
        for (var y = 0; y < first.GetHeight(); y++)
        {
            for (var x = xMin; x < xMax; x++)
            {
                var offset = ((y * first.GetWidth()) + x) * bytesPerPixel;
                var delta = Math.Abs(firstData[offset] - secondData[offset])
                    + Math.Abs(firstData[offset + 1] - secondData[offset + 1])
                    + Math.Abs(firstData[offset + 2] - secondData[offset + 2]);
                if (delta > 30)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Counts exact RGB changes within a clipped viewport rectangle.</summary>
    /// <param name="before">Frame before the transition.</param>
    /// <param name="after">Frame after the transition.</param>
    /// <param name="region">Viewport rectangle to compare.</param>
    /// <returns>The changed-pixel count.</returns>
    public static int CountRgbChanges(Image before, Image after, Rect2 region)
    {
        var changed = 0;
        var minX = Math.Max(0, (int)MathF.Floor(region.Position.X));
        var maxX = Math.Min(after.GetWidth() - 1, (int)MathF.Ceiling(region.End.X));
        var minY = Math.Max(0, (int)MathF.Floor(region.Position.Y));
        var maxY = Math.Min(after.GetHeight() - 1, (int)MathF.Ceiling(region.End.Y));
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (ProbePixels.DifferentRgb(before.GetPixel(x, y), after.GetPixel(x, y)))
                {
                    changed++;
                }
            }
        }

        return changed;
    }

    /// <summary>Counts pixels whose three channels sum above the historical probe threshold.</summary>
    /// <param name="image">Framebuffer image.</param>
    /// <param name="xMin">Inclusive left pixel.</param>
    /// <param name="xMax">Exclusive right pixel.</param>
    /// <returns>The bright-pixel count.</returns>
    public static int CountBright(Image image, int xMin, int xMax)
    {
        var data = image.GetData();
        var bytesPerPixel = image.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        var count = 0;
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = xMin; x < xMax; x++)
            {
                var offset = ((y * image.GetWidth()) + x) * bytesPerPixel;
                if (data[offset] + data[offset + 1] + data[offset + 2] > 120)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Counts bright pixels clipped to a viewport rectangle.</summary>
    /// <param name="image">Framebuffer image.</param>
    /// <param name="rectangle">Viewport rectangle, in pixels.</param>
    /// <returns>The bright-pixel count.</returns>
    public static int CountBright(Image image, Rect2 rectangle)
    {
        var count = 0;
        var fromX = Math.Max(0, (int)rectangle.Position.X);
        var toX = Math.Min(image.GetWidth(), (int)rectangle.End.X);
        var fromY = Math.Max(0, (int)rectangle.Position.Y);
        var toY = Math.Min(image.GetHeight(), (int)rectangle.End.Y);
        for (var y = fromY; y < toY; y++)
        {
            for (var x = fromX; x < toX; x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.R + pixel.G + pixel.B > 0.45f)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Finds the bounds of every RGB difference between equal-sized images.</summary>
    /// <param name="first">First image.</param>
    /// <param name="second">Second image.</param>
    /// <returns>The changed bounds, or null when the images match.</returns>
    public static Rect2? DifferenceBounds(Image first, Image second) => Bounds(
        first,
        (x, y) => ProbePixels.DifferentRgb(first.GetPixel(x, y), second.GetPixel(x, y)));

    /// <summary>Finds source-raster differences whose combined RGB delta exceeds a noise threshold.</summary>
    /// <param name="first">Frame containing the tested fighter.</param>
    /// <param name="second">Matching frame with that fighter hidden.</param>
    /// <param name="threshold">Strict minimum combined 8-bit RGB delta for one selected pixel.</param>
    /// <returns>The selected bounds, or <see langword="null"/> when no pixel exceeds the threshold.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="threshold"/> is negative.</exception>
    public static Rect2? DifferenceBoundsAboveThreshold(Image first, Image second, int threshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);
        ProbeRequirement.Require(first.GetSize() == second.GetSize(), "difference images must share dimensions");
        return Bounds(first, (x, y) => RgbDelta(first.GetPixel(x, y), second.GetPixel(x, y)) > threshold);
    }

    /// <summary>Finds Kotemon's warm armour pixels in a source-size command frame.</summary>
    /// <param name="image">Source-size 320x240 frame.</param>
    /// <returns>The warm armour bounds, or null when no matching texel exists.</returns>
    public static Rect2? WarmKotemonBounds(Image image)
    {
        var limits = new Rect2I(80, 55, 165, 175);
        return Bounds(image, limits, (x, y) =>
        {
            var pixel = image.GetPixel(x, y);
            return pixel.R8 > 70 && pixel.R8 > pixel.G8 * 1.2f && pixel.R8 > pixel.B8 * 1.15f;
        });
    }

    /// <summary>Returns mean BT.601 luminance on the 0..255 scale.</summary>
    /// <param name="image">Framebuffer image.</param>
    /// <returns>The mean luminance.</returns>
    public static double MeanLuminance(Image image)
    {
        double total = 0;
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                total += Luminance(image.GetPixel(x, y));
            }
        }

        return total / (image.GetWidth() * image.GetHeight());
    }

    /// <summary>Separates fighter and stage luminance from a full-minus-stage pair.</summary>
    /// <param name="full">Frame with fighters.</param>
    /// <param name="stage">Matching frame with fighters hidden.</param>
    /// <returns>Fighter, stage and whole-frame means on the 0..255 scale.</returns>
    public static (double Fighter, double Stage, double Mean) FighterAndStageLuminance(Image full, Image stage)
    {
        ProbeRequirement.Require(full.GetSize() == stage.GetSize(), "entrance images must share dimensions");
        var sums = AccumulateLuminance(full, stage);
        ProbeRequirement.Require(sums.FighterCount > 0, "entrance fighter difference mask is empty");
        ProbeRequirement.Require(sums.StageCount > 0, "entrance stage difference complement is empty");
        var pixels = full.GetWidth() * full.GetHeight();
        return (sums.Fighter / sums.FighterCount, sums.Stage / sums.StageCount, sums.Total / pixels);
    }

    /// <summary>Accumulates source-scale luminance over fighter differences and the unchanged stage.</summary>
    /// <param name="full">Frame containing the fighters.</param>
    /// <param name="stage">Matching frame with fighters hidden.</param>
    /// <returns>Luminance sums and pixel counts for both masks.</returns>
    private static (double Fighter, double Stage, double Total, int FighterCount, int StageCount) AccumulateLuminance(
        Image full,
        Image stage)
    {
        double fighter = 0;
        double scene = 0;
        double total = 0;
        var fighterCount = 0;
        var stageCount = 0;
        for (var y = 0; y < full.GetHeight(); y++)
        {
            for (var x = 0; x < full.GetWidth(); x++)
            {
                var pixel = full.GetPixel(x, y);
                var background = stage.GetPixel(x, y);
                var luminance = Luminance(pixel);
                total += luminance;
                if (ColorDistance(pixel, background) > 0.08f)
                {
                    fighter += luminance;
                    fighterCount++;
                }
                else
                {
                    scene += luminance;
                    stageCount++;
                }
            }
        }

        return (fighter, scene, total, fighterCount, stageCount);
    }

    /// <summary>Measures selected pixels over the complete image.</summary>
    /// <param name="image">Image whose extent bounds the search.</param>
    /// <param name="selected">Pixel predicate in image coordinates.</param>
    /// <returns>The inclusive selected extent, or <see langword="null"/> when empty.</returns>
    private static Rect2? Bounds(Image image, Func<int, int, bool> selected) =>
        Bounds(image, new Rect2I(Vector2I.Zero, image.GetSize()), selected);

    /// <summary>Measures selected pixels inside a clipped search rectangle.</summary>
    /// <param name="image">Image whose dimensions initialize the empty extent.</param>
    /// <param name="limits">Half-open pixel rectangle to inspect.</param>
    /// <param name="selected">Pixel predicate in image coordinates.</param>
    /// <returns>The inclusive selected extent, or <see langword="null"/> when empty.</returns>
    private static Rect2? Bounds(Image image, Rect2I limits, Func<int, int, bool> selected)
    {
        var minimum = image.GetSize();
        var maximum = new Vector2I(-1, -1);
        for (var y = limits.Position.Y; y < limits.End.Y; y++)
        {
            for (var x = limits.Position.X; x < limits.End.X; x++)
            {
                if (selected(x, y))
                {
                    minimum = new Vector2I(Math.Min(minimum.X, x), Math.Min(minimum.Y, y));
                    maximum = new Vector2I(Math.Max(maximum.X, x), Math.Max(maximum.Y, y));
                }
            }
        }

        return maximum.X < minimum.X
            ? null
            : new Rect2(minimum, maximum - minimum + Vector2I.One);
    }

    /// <summary>Measures combined absolute RGB difference in 8-bit framebuffer space.</summary>
    /// <param name="first">First framebuffer colour.</param>
    /// <param name="second">Second framebuffer colour.</param>
    /// <returns>Combined absolute RGB delta.</returns>
    private static int RgbDelta(Color first, Color second) =>
        Math.Abs(first.R8 - second.R8) + Math.Abs(first.G8 - second.G8) + Math.Abs(first.B8 - second.B8);

    /// <summary>Converts a normalized RGB color to the probe's 0..255 luminance scale.</summary>
    /// <param name="pixel">Normalized source color.</param>
    /// <returns>Weighted luminance on the 0..255 scale.</returns>
    private static double Luminance(Color pixel) =>
        (pixel.R * 54.213) + (pixel.G * 182.376) + (pixel.B * 18.411);

    /// <summary>Measures RGB Manhattan distance between normalized colors.</summary>
    /// <param name="first">First normalized color.</param>
    /// <param name="second">Second normalized color.</param>
    /// <returns>Sum of absolute RGB-channel differences.</returns>
    private static float ColorDistance(Color first, Color second) =>
        Math.Abs(first.R - second.R) + Math.Abs(first.G - second.G) + Math.Abs(first.B - second.B);
}
