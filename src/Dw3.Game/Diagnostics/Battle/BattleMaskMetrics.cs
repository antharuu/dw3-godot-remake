using Dw3Production.Diagnostics.Field;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Builds and measures binary 320x240 masks shared by the battle lighting probes. A selected
/// pixel is white, every other pixel is black, and a red channel of at least one half selects.
/// </summary>
internal static class BattleMaskMetrics
{
    /// <summary>Source picture width in pixels.</summary>
    public const int Width = 320;

    /// <summary>Source picture height in pixels.</summary>
    public const int Height = 240;

    /// <summary>Creates a cleared source-sized binary mask.</summary>
    /// <returns>A 320 by 240 black RGBA mask.</returns>
    public static Image CreateEmpty()
    {
        var mask = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        mask.Fill(Colors.Black);
        return mask;
    }

    /// <summary>Builds a white mask where two source-sized images differ visibly.</summary>
    /// <param name="model">Image with the fighter shown.</param>
    /// <param name="stage">Image with the fighter hidden.</param>
    /// <returns>Binary difference mask.</returns>
    public static Image Difference(Image model, Image stage)
    {
        var mask = CreateEmpty();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (ChannelDistance(model.GetPixel(x, y), stage.GetPixel(x, y)) > .08f)
                {
                    mask.SetPixel(x, y, Colors.White);
                }
            }
        }

        return mask;
    }

    /// <summary>Measures the luminance distribution inside a binary mask.</summary>
    /// <param name="image">Image to sample.</param>
    /// <param name="mask">Selection mask.</param>
    /// <param name="minimumPixels">Minimum accepted selection size.</param>
    /// <returns>Count, average, quantiles, and eight-bin histogram.</returns>
    /// <exception cref="InvalidOperationException">The mask selects fewer than <paramref name="minimumPixels"/> pixels.</exception>
    public static BattleLuminanceStats Measure(Image image, Image mask, int minimumPixels)
    {
        var values = new List<float>();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (IsSelected(mask, x, y))
                {
                    values.Add(Luminance(image.GetPixel(x, y)));
                }
            }
        }

        ProbeRequirement.Require(values.Count >= minimumPixels, $"luminance mask too small pixels={values.Count} minimum={minimumPixels}");
        values.Sort();
        return new BattleLuminanceStats(values.Count, values.Average(), Quantile(values, .1f), Quantile(values, .5f), Quantile(values, .9f), Histogram(values));
    }

    /// <summary>Returns whether a mask pixel is selected.</summary>
    /// <param name="mask">Binary mask.</param>
    /// <param name="x">Pixel column.</param>
    /// <param name="y">Pixel row.</param>
    /// <returns>True when the red channel is at least one half.</returns>
    public static bool IsSelected(Image mask, int x, int y) => mask.GetPixel(x, y).R >= .5f;

    /// <summary>Computes the source-compatible luminance value.</summary>
    /// <param name="color">Colour to measure.</param>
    /// <returns>Weighted luminance in 0 through 255 scale.</returns>
    public static float Luminance(Color color) => (color.R * 54.213f) + (color.G * 182.376f) + (color.B * 18.411f);

    /// <summary>Computes the symmetric relative difference between two nonnegative luminance values.</summary>
    /// <param name="first">First value.</param>
    /// <param name="second">Second value.</param>
    /// <returns>Difference normalized by their average.</returns>
    public static float RelativeDelta(float first, float second) => MathF.Abs(first - second) / MathF.Max(1f, (first + second) * .5f);

    /// <summary>Sums the absolute RGB channel differences of two colours.</summary>
    /// <param name="first">First colour.</param>
    /// <param name="second">Second colour.</param>
    /// <returns>Channel distance in normalized units.</returns>
    private static float ChannelDistance(Color first, Color second) =>
        Mathf.Abs(first.R - second.R) + Mathf.Abs(first.G - second.G) + Mathf.Abs(first.B - second.B);

    /// <summary>Gets a rounded quantile from sorted luminance values.</summary>
    /// <param name="values">Sorted values.</param>
    /// <param name="fraction">Quantile fraction.</param>
    /// <returns>The selected value.</returns>
    private static float Quantile(List<float> values, float fraction) =>
        values[(int)Mathf.Clamp(Mathf.Round((values.Count - 1) * fraction), 0, values.Count - 1)];

    /// <summary>Builds the stable eight-bin luminance histogram string.</summary>
    /// <param name="values">Measured luminance values.</param>
    /// <returns>Comma-separated bin counts.</returns>
    private static string Histogram(IEnumerable<float> values)
    {
        var bins = new int[8];
        foreach (var value in values)
        {
            bins[Math.Min(7, (int)(value / 32f))]++;
        }

        return string.Join(',', bins);
    }
}
