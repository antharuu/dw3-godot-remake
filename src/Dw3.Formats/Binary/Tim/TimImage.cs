namespace Dw3.Formats.Binary.Tim;

/// <summary>
/// A decoded TIM image: raw RGBA pixel data and dimensions.
///
/// Pixel layout: R, G, B, A bytes per pixel, row-major, left-to-right,
/// top-to-bottom.  <see cref="Pixels"/>.Length == Width * Height * 4.
/// </summary>
public sealed class TimImage
{
    /// <summary>Initializes a new instance of the <see cref="TimImage"/> class; called only by <see cref="TimDecoder"/>.</summary>
    /// <param name="width">Image width in pixels; must be positive.</param>
    /// <param name="height">Image height in pixels; must be positive.</param>
    /// <param name="pixels">RGBA pixel data; must have exactly <c>width * height * 4</c> bytes.</param>
    /// <param name="mode">TIM pixel mode that produced this image.</param>
    /// <param name="clutRowCount">Number of CLUT rows in the source TIM; 0 for 16-bit direct images.</param>
    internal TimImage(int width, int height, byte[] pixels, TimPixelMode mode, int clutRowCount)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        Mode = mode;
        ClutRowCount = clutRowCount;
    }

    /// <summary>Gets image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets image height in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// Gets raw RGBA pixel data: 4 bytes (R, G, B, A) per pixel, row-major.
    /// Length is exactly <c>Width * Height * 4</c>.
    /// </summary>
    public byte[] Pixels { get; }

    /// <summary>Gets the TIM pixel mode (4-bit, 8-bit or 16-bit) the image was decoded from.</summary>
    public TimPixelMode Mode { get; }

    /// <summary>
    /// Gets number of CLUT rows in the source TIM file.
    /// 0 for 16-bit direct images (no CLUT).
    /// </summary>
    public int ClutRowCount { get; }
}
