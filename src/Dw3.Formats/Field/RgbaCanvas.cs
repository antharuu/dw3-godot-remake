namespace Dw3.Formats.Field;

/// <summary>
/// A mutable RGBA pixel canvas used to composite one or more <see cref="RgbaImage"/> sprites
/// into a single flat image. Pixels are in row-major order, 4 bytes per pixel (R, G, B, A).
/// </summary>
internal sealed class RgbaCanvas
{
    /// <summary>Initializes a new instance of the <see cref="RgbaCanvas"/> class with all pixels set to transparent black.</summary>
    /// <param name="width">Canvas width in pixels; must be positive.</param>
    /// <param name="height">Canvas height in pixels; must be positive.</param>
    public RgbaCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new byte[checked(width * height * 4)];
    }

    /// <summary>Gets the canvas width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the canvas height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the raw RGBA pixel buffer; length is exactly Width × Height × 4.</summary>
    public byte[] Pixels { get; }

    /// <summary>
    /// Composites <paramref name="source"/> onto the canvas at (<paramref name="x"/>, <paramref name="y"/>),
    /// skipping fully transparent pixels (alpha == 0).
    /// </summary>
    /// <param name="source">Source image to composite onto this canvas.</param>
    /// <param name="x">Left coordinate within the canvas; must be non-negative.</param>
    /// <param name="y">Top coordinate within the canvas; must be non-negative.</param>
    /// <exception cref="FormatException">The coordinates are negative or the source does not intersect the canvas.</exception>
    public void Composite(RgbaImage source, int x, int y)
    {
        if (x < 0 || y < 0)
        {
            throw new FormatException("Field sprite has a negative destination coordinate.");
        }

        int clippedWidth = Math.Min(source.Width, Width - x);
        int clippedHeight = Math.Min(source.Height, Height - y);
        if (clippedWidth <= 0 || clippedHeight <= 0)
        {
            throw new FormatException("Field sprite does not intersect its destination layer.");
        }

        for (int row = 0; row < clippedHeight; row++)
        {
            for (int column = 0; column < clippedWidth; column++)
            {
                int sourceOffset = (row * source.Width + column) * 4;
                byte alpha = source.Pixels[sourceOffset + 3];
                if (alpha == 0)
                {
                    continue;
                }

                int targetOffset = ((y + row) * Width + x + column) * 4;
                source.Pixels.AsSpan(sourceOffset, 4).CopyTo(Pixels.AsSpan(targetOffset, 4));
            }
        }
    }
}
