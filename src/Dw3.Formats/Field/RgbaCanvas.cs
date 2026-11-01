namespace Dw3.Formats.Field;

internal sealed class RgbaCanvas
{
    public RgbaCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new byte[checked(width * height * 4)];
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

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
