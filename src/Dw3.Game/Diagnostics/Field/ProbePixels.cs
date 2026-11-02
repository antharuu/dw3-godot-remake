using Godot;

namespace Dw3Production.Diagnostics.Field;

/// <summary>Colour tests shared by the pixel-level probe assertions.</summary>
internal static class ProbePixels
{
    /// <summary>Per-channel tolerance in the 0-1 colour range, about 2.5 of 255, absorbing filtering noise.</summary>
    private const float ChannelTolerance = 0.01f;

    /// <summary>Alpha from which a decoded sprite texel counts as opaque; semi-transparent texels are skipped.</summary>
    private const float OpaqueAlpha = 0.99f;

    /// <summary>Tells whether two colours match on red, green and blue within the probe tolerance; alpha is ignored.</summary>
    /// <param name="left">First colour.</param>
    /// <param name="right">Second colour.</param>
    /// <returns><see langword="true"/> when every RGB channel differs by at most the tolerance.</returns>
    public static bool SameRgb(Color left, Color right) => !DifferentRgb(left, right);

    /// <summary>Tells whether two colours differ on red, green or blue by more than the probe tolerance.</summary>
    /// <param name="left">First colour.</param>
    /// <param name="right">Second colour.</param>
    /// <returns><see langword="true"/> when one RGB channel differs by more than the tolerance.</returns>
    public static bool DifferentRgb(Color left, Color right) =>
        MathF.Abs(left.R - right.R) > ChannelTolerance || MathF.Abs(left.G - right.G) > ChannelTolerance || MathF.Abs(left.B - right.B) > ChannelTolerance;

    /// <summary>Tells whether a decoded sprite texel is drawn fully opaque.</summary>
    /// <param name="color">Texel colour.</param>
    /// <returns><see langword="true"/> when alpha is at least 0.99.</returns>
    public static bool IsOpaque(Color color) => !(color.A < OpaqueAlpha);

    /// <summary>Tells whether a pixel lies inside an image, keeping <paramref name="margin"/> pixels from each edge.</summary>
    /// <param name="image">Image whose bounds are tested.</param>
    /// <param name="x">Pixel column.</param>
    /// <param name="y">Pixel row.</param>
    /// <param name="margin">Pixels excluded along every edge; 0 accepts the whole image.</param>
    /// <returns><see langword="true"/> when the pixel can be read.</returns>
    public static bool Contains(Image image, int x, int y, int margin = 0) =>
        x >= margin && y >= margin && x < image.GetWidth() - margin && y < image.GetHeight() - margin;

    /// <summary>Lists the opaque texels of a decoded sprite, row by row from the top-left texel.</summary>
    /// <param name="image">Decoded sprite frame.</param>
    /// <returns>Each opaque texel with its column, row and colour.</returns>
    public static IEnumerable<(int X, int Y, Color Color)> OpaqueTexels(Image image)
    {
        var width = image.GetWidth();
        var height = image.GetHeight();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = image.GetPixel(x, y);
                if (IsOpaque(color))
                {
                    yield return (x, y, color);
                }
            }
        }
    }

    /// <summary>Loads a decoded PNG, returning <see langword="null"/> when it is missing or empty.</summary>
    /// <param name="path">Absolute path in the data pack.</param>
    /// <returns>The image, or <see langword="null"/>.</returns>
    public static Image? TryLoad(string path)
    {
        var image = Image.LoadFromFile(path);
        return image is null || image.IsEmpty() ? null : image;
    }
}
