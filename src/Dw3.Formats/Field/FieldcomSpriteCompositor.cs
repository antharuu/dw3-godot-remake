using System.Buffers.Binary;
using Dw3.Formats.Water;

namespace Dw3.Formats.Field;

/// <summary>Composes FIELDCOM 4-bit frames using the archive's page-local CLUT layout.</summary>
public static class FieldcomSpriteCompositor
{
    private const int PageWords = 64;
    private const int PageTexels = 256;
    private const int MaximumParts = 16;

    /// <summary>
    /// Composites one FIELDCOM sprite frame from its part list and the decoded VRAM pages,
    /// producing a single RGBA image with its source UV origin.
    /// </summary>
    /// <param name="parts">Sprite part table decoded from the FIELDCOM sprite sheet.</param>
    /// <param name="frame">Raw 6-byte frame record (u16 count, u16 dx, u16 dy + one 6-byte slot per part).</param>
    /// <param name="vram">Decoded FIELDCOM VRAM containing the 15-bit pixel data.</param>
    /// <returns>The composited sprite frame as an RGBA image with its UV offset origin.</returns>
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

    /// <summary>Places each part in the composed frame canvas, reading UV from the VRAM page.</summary>
    /// <param name="parts">List of sprite parts defining UV and dimensions.</param>
    /// <param name="frame">Raw frame bytes beginning at the part-count field.</param>
    /// <param name="vram">FIELDCOM VRAM pages used to sample texels.</param>
    /// <param name="count">Number of parts to read from the frame.</param>
    /// <param name="row">CLUT palette row used to resolve 4-bit texel indices.</param>
    /// <returns>Array of placed-part descriptors with sampled pixel data.</returns>
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

    /// <summary>Computes the bounding box from placed parts and blends them into the output pixels.</summary>
    /// <param name="placed">Array of placed-part descriptors in back-to-front order.</param>
    /// <param name="count">Number of valid entries in <paramref name="placed"/>.</param>
    /// <param name="abr">Semi-transparency mode from the frame header.</param>
    /// <returns>Assembled FIELDCOM sprite image with word-colour pixel data.</returns>
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

    /// <summary>Back-to-front composites each part into the output word buffer.</summary>
    /// <param name="placed">Array of placed-part descriptors.</param>
    /// <param name="count">Number of valid entries in <paramref name="placed"/>.</param>
    /// <param name="words">Destination word buffer sized to the bounding box.</param>
    /// <param name="width">Width in words of the bounding box.</param>
    /// <param name="minX">Left edge of the bounding box.</param>
    /// <param name="minY">Top edge of the bounding box.</param>
    private static void BlendParts((int X, int Y, int Width, int Height, ushort[] Words)[] placed, int count, ushort[] words, int width, int minX, int minY)
    {
        for (int index = count - 1; index >= 0; index--)
        {
            BlendPart(placed[index], words, width, minX, minY);
        }
    }

    /// <summary>Composites the pixels of one part back-to-front into the destination word buffer.</summary>
    /// <param name="part">Placed-part descriptor with sampled pixel data.</param>
    /// <param name="words">Destination word buffer.</param>
    /// <param name="width">Bounding-box width used to compute destination offsets.</param>
    /// <param name="minX">Bounding-box left edge.</param>
    /// <param name="minY">Bounding-box top edge.</param>
    private static void BlendPart((int X, int Y, int Width, int Height, ushort[] Words) part, ushort[] words, int width, int minX, int minY)
    {
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

    /// <summary>Samples a 4-bit indexed sprite part from the VRAM page using the CLUT row and page-local UV.</summary>
    /// <param name="part">Sprite part with UV coordinates and mode flags.</param>
    /// <param name="vram">FIELDCOM VRAM pages to sample from.</param>
    /// <param name="row">CLUT palette row added to the part's ClutY coordinate.</param>
    /// <returns>Decoded word-colour pixels for the part rectangle.</returns>
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

    /// <summary>Reads texels from a raw VRAM page and returns decoded word-colour pixels for the part.</summary>
    /// <param name="page">Raw VRAM page as a flat array of PS1 word colours.</param>
    /// <param name="part">Sprite part with dimensions and ClutX coordinate.</param>
    /// <param name="localU">Page-local horizontal texel offset.</param>
    /// <param name="paletteRow">Absolute CLUT palette row within the page.</param>
    /// <returns>Decoded word-colour pixels at the part's width × height.</returns>
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
