using System.Buffers.Binary;
using Dw3.Formats.Water;

namespace Dw3.Formats.Field;

/// <summary>Composes FIELDCOM 4-bit frames using the archive's page-local CLUT layout.</summary>
public static class FieldcomSpriteCompositor
{
    private const int PageWords = 64;
    private const int PageTexels = 256;
    private const int MaximumParts = 16;

    /// <exception cref="FormatException">The frame or part references unavailable FIELDCOM data.</exception>
    public static FieldcomSpriteImage Compose(IReadOnlyList<SpritePart> parts, ReadOnlySpan<byte> frame, FieldcomVram vram)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(vram);
        if (frame.Length < 6)
        {
            throw new FormatException("FIELDCOM sprite frame is truncated");
        }

        int count = BinaryPrimitives.ReadInt16LittleEndian(frame);
        int row = BinaryPrimitives.ReadInt16LittleEndian(frame[2..]);
        int abr = BinaryPrimitives.ReadInt16LittleEndian(frame[4..]);
        if (count <= 0 || count > MaximumParts || frame.Length < 6 + count * 6)
        {
            throw new FormatException("FIELDCOM sprite frame part count is invalid");
        }

        var placed = PlaceParts(parts, frame, vram, count, row);
        return AssembleImage(placed, count, abr);
    }

    private static (int X, int Y, int Width, int Height, ushort[] Words)[] PlaceParts(
        IReadOnlyList<SpritePart> parts, ReadOnlySpan<byte> frame, FieldcomVram vram, int count, int row)
    {
        var placed = new (int X, int Y, int Width, int Height, ushort[] Words)[count];
        for (int index = 0; index < count; index++)
        {
            int partIndex = BinaryPrimitives.ReadInt16LittleEndian(frame[(6 + index * 6)..]);
            if (partIndex < 0 || partIndex >= parts.Count)
            {
                throw new FormatException("FIELDCOM sprite frame references missing part");
            }

            SpritePart part = parts[partIndex];
            placed[index] = (
                BinaryPrimitives.ReadInt16LittleEndian(frame[(8 + index * 6)..]),
                BinaryPrimitives.ReadInt16LittleEndian(frame[(10 + index * 6)..]),
                part.Width,
                part.Height,
                Sample(part, vram, row));
        }

        return placed;
    }

    private static FieldcomSpriteImage AssembleImage((int X, int Y, int Width, int Height, ushort[] Words)[] placed, int count, int abr)
    {
        int minX = placed.Min(item => item.X);
        int minY = placed.Min(item => item.Y);
        int width = placed.Max(item => item.X + item.Width) - minX;
        int height = placed.Max(item => item.Y + item.Height) - minY;
        var words = new ushort[width * height];
        BlendParts(placed, count, words, width, minX, minY);
        return new FieldcomSpriteImage(width, height, minX, minY, abr, words);
    }

    private static void BlendParts((int X, int Y, int Width, int Height, ushort[] Words)[] placed, int count, ushort[] words, int width, int minX, int minY)
    {
        for (int index = count - 1; index >= 0; index--)
        {
            var part = placed[index];
            for (int y = 0; y < part.Height; y++)
            {
                for (int x = 0; x < part.Width; x++)
                {
                    ushort word = part.Words[y * part.Width + x];
                    if (word != 0)
                    {
                        words[(part.Y - minY + y) * width + part.X - minX + x] = word;
                    }
                }
            }
        }
    }

    private static ushort[] Sample(SpritePart part, FieldcomVram vram, int row)
    {
        if (part.Mode != 0 || part.U < 0 || part.V < 0 || part.Width <= 0 || part.Height <= 0 ||
            part.ClutX < 0 || part.ClutY < 0 || part.V + part.Height > 256)
        {
            throw new FormatException("FIELDCOM sprite part bounds are invalid");
        }

        int texturePage = part.U / PageTexels;
        int localU = part.U % PageTexels;
        if (texturePage >= vram.Pages.Count || localU + part.Width > PageTexels)
        {
            throw new FormatException("FIELDCOM 4-bit sprite references unavailable VRAM page");
        }

        int paletteRow = part.ClutY + row;
        if (paletteRow < 0 || paletteRow >= 256 || part.ClutX + 16 > PageWords)
        {
            throw new FormatException("FIELDCOM sprite palette is outside VRAM");
        }

        return SamplePage(vram.Pages[texturePage], part, localU, paletteRow);
    }

    private static ushort[] SamplePage(ushort[] page, SpritePart part, int localU, int paletteRow)
    {
        var result = new ushort[part.Width * part.Height];
        for (int y = 0; y < part.Height; y++)
        {
            for (int x = 0; x < part.Width; x++)
            {
                int u = localU + x;
                ushort packed = page[(part.V + y) * PageWords + u / 4];
                int paletteIndex = (packed >> ((u & 3) * 4)) & 15;
                result[y * part.Width + x] = page[paletteRow * PageWords + part.ClutX + paletteIndex];
            }
        }

        return result;
    }
}
