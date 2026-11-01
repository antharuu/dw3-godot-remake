namespace Dw3.Formats.Binary.Tim;

/// <summary>
/// A decoded TIM image: raw RGBA pixel data and dimensions.
///
/// Pixel layout: R, G, B, A bytes per pixel, row-major, left-to-right,
/// top-to-bottom.  <see cref="Pixels"/>.Length == Width * Height * 4.
/// </summary>
public sealed class TimImage
{
    /// <summary>Gets image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets image height in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// Gets raw RGBA pixel data: 4 bytes (R, G, B, A) per pixel, row-major.
    /// Length is exactly <c>Width * Height * 4</c>.
    /// </summary>
    public byte[] Pixels { get; }

    /// <summary>Gets tIM pixel mode that produced this image.</summary>
    public TimPixelMode Mode { get; }

    /// <summary>
    /// Gets number of CLUT rows in the source TIM file.
    /// 0 for 16-bit direct images (no CLUT).
    /// </summary>
    public int ClutRowCount { get; }

    internal TimImage(int width, int height, byte[] pixels, TimPixelMode mode, int clutRowCount)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        Mode = mode;
        ClutRowCount = clutRowCount;
    }
}

/// <summary>PSX TIM pixel modes supported by this decoder.</summary>
public enum TimPixelMode
{
    /// <summary>
    /// 4-bit indexed colour.
    /// CLUT has 16 entries; pixels are packed two per byte (low nibble = left pixel).
    /// Words-per-line in the image block must be multiplied by 4 to get pixel width.
    /// TIM flags field = 0x08.
    /// </summary>
    Indexed4Bit,

    /// <summary>
    /// 8-bit indexed colour.
    /// CLUT has 256 entries; one byte per pixel.
    /// Words-per-line must be multiplied by 2 to get pixel width.
    /// TIM flags field = 0x09.
    /// </summary>
    Indexed8Bit,

    /// <summary>
    /// 15-bit direct colour (5-5-5 BGR), one 16-bit word per pixel.
    /// No CLUT.  Words-per-line equals pixel width.
    /// TIM flags field = 0x02.
    /// </summary>
    Direct15Bit,
}
