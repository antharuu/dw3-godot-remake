using System.Buffers.Binary;

namespace Dw3.Formats.Water;

/// <summary>
/// Composes a sprite-sheet frame from its parts sampled in the object VRAM.
///
/// Frame layout (s16 little-endian): part count (1..16), palette row shift, blend mode,
/// then per part: part index, x, y. Parts are emitted in reverse order, so the first
/// listed part ends on top; transparent texels never overwrite.
///
/// 4-bit parts read their 16-colour palette from VRAM at
/// (0x140 + ClutX, 0x100 + ClutY + row shift + palette row), inside the loaded pages.
/// 8-bit parts read the alternate palette row ClutY + row shift + palette row - 0x100.
/// </summary>
public static class SpriteFrameRenderer
{
    private const int MaximumParts = 16;
    private const int FourBitPageTexels = 256;
    private const int EightBitPageTexels = 128;
    private const int VramPaletteOriginX = 0x140;
    private const int VramPaletteOriginY = 0x100;
    private const int VramHeight = 0x200;
    private const int FourBitPaletteSize = 16;

    /// <summary>Renders the frame with the palette shifted by <paramref name="clutRow"/>.</summary>
    /// <exception cref="FormatException">The frame or one of its parts references data outside the loaded VRAM.</exception>
    public static SpriteFrameImage Render(IReadOnlyList<SpritePart> parts, ReadOnlySpan<byte> frame, ObjectVram vram, int clutRow)
    {
        (int count, int frameRow, int abr) = ReadFrameHeader(frame);
        (int X, int Y, int Width, int Height, ushort[] Words)[] placed = PlaceParts(parts, frame, vram, count, frameRow + clutRow);
        int minX = placed.Min(item => item.X);
        int minY = placed.Min(item => item.Y);
        int width = placed.Max(item => item.X + item.Width) - minX;
        int height = placed.Max(item => item.Y + item.Height) - minY;
        return new SpriteFrameImage(width, height, minX, minY, abr, Compose(placed, width, height, minX, minY));
    }

    /// <summary>Reads the three signed words at the frame head and verifies its part records are present.</summary>
    /// <param name="frame">Source frame beginning with count, CLUT shift and blend mode.</param>
    /// <returns>The bounded count, palette-row shift and PS1 blend mode.</returns>
    /// <exception cref="FormatException">The frame head is truncated or its part count is unsupported.</exception>
    private static (int Count, int FrameRow, int Abr) ReadFrameHeader(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 6)
        {
            throw new FormatException("sprite frame truncated");
        }

        int count = BinaryPrimitives.ReadInt16LittleEndian(frame);
        if (count <= 0 || count > MaximumParts)
        {
            throw new FormatException("sprite frame part count invalid");
        }

        if (frame.Length < 6 + (6 * count))
        {
            throw new FormatException("sprite frame truncated");
        }

        return (count, BinaryPrimitives.ReadInt16LittleEndian(frame[2..]), BinaryPrimitives.ReadInt16LittleEndian(frame[4..]));
    }

    /// <summary>Samples every source part at its signed frame placement and resolved CLUT row.</summary>
    /// <param name="parts">Indexed sprite-sheet parts referenced by the frame.</param>
    /// <param name="frame">Validated frame bytes with six-byte placement records.</param>
    /// <param name="vram">Loaded object texture pages and alternate palettes.</param>
    /// <param name="count">Number of part placement records.</param>
    /// <param name="paletteRow">Combined frame and caller palette-row shift.</param>
    /// <returns>Sampled texture words with their local frame coordinates.</returns>
    /// <exception cref="FormatException">A placement names no sprite-sheet part.</exception>
    private static (int X, int Y, int Width, int Height, ushort[] Words)[] PlaceParts(IReadOnlyList<SpritePart> parts, ReadOnlySpan<byte> frame, ObjectVram vram, int count, int paletteRow)
    {
        var placed = new (int X, int Y, int Width, int Height, ushort[] Words)[count];
        for (int index = 0; index < count; index++)
        {
            int offset = 6 + (6 * index);
            int partIndex = BinaryPrimitives.ReadInt16LittleEndian(frame[offset..]);
            if (partIndex < 0 || partIndex >= parts.Count)
            {
                throw new FormatException("sprite frame references missing part");
            }

            SpritePart part = parts[partIndex];
            placed[index] = (BinaryPrimitives.ReadInt16LittleEndian(frame[(offset + 2)..]), BinaryPrimitives.ReadInt16LittleEndian(frame[(offset + 4)..]), part.Width, part.Height, Sample(part, vram, paletteRow));
        }

        return placed;
    }

    /// <summary>Overlays sampled parts in reverse source order, retaining each non-transparent PS1 word.</summary>
    /// <param name="placed">Sampled parts with coordinates relative to the source frame.</param>
    /// <param name="width">Output span in words.</param>
    /// <param name="height">Output row count in pixels.</param>
    /// <param name="minX">Left output origin in source coordinates.</param>
    /// <param name="minY">Top output origin in source coordinates.</param>
    /// <returns>Composed 16-bit PS1 pixels in row-major order.</returns>
    private static ushort[] Compose((int X, int Y, int Width, int Height, ushort[] Words)[] placed, int width, int height, int minX, int minY)
    {
        var words = new ushort[width * height];
        for (int index = placed.Length - 1; index >= 0; index--)
        {
            var (x, y, partWidth, partHeight, partWords) = placed[index];
            OverlayPart(words, width, (minX, minY), (x, y), (partWidth, partHeight), partWords);
        }

        return words;
    }

    /// <summary>Writes the opaque texels of one sampled part into its destination rectangle.</summary>
    /// <param name="destination">Output pixels in row-major order.</param>
    /// <param name="destinationWidth">Output span in pixels.</param>
    /// <param name="origin">Left and top source coordinates of the output.</param>
    /// <param name="position">Left and top source coordinates of the part.</param>
    /// <param name="size">Width and height of the part in pixels.</param>
    /// <param name="source">Sampled PS1 pixel words for the part.</param>
    private static void OverlayPart(ushort[] destination, int destinationWidth, (int X, int Y) origin, (int X, int Y) position, (int Width, int Height) size, ushort[] source)
    {
        for (int row = 0; row < size.Height; row++)
        {
            for (int column = 0; column < size.Width; column++)
            {
                ushort word = source[(row * size.Width) + column];
                if (word != 0)
                {
                    destination[((position.Y - origin.Y + row) * destinationWidth) + position.X - origin.X + column] = word;
                }
            }
        }
    }

    private static ushort[] Sample(SpritePart part, ObjectVram vram, int rowShift)
    {
        if (Math.Min(Math.Min(part.U, part.V), Math.Min(Math.Min(part.Width, part.Height), Math.Min(part.ClutX, part.ClutY))) < 0 ||
            part.Width <= 0 || part.Height <= 0 || part.V + part.Height > ObjectTextureArchiveDecoder.PageRows)
            throw new FormatException("sprite part bounds invalid");
        return part.Mode switch
        {
            0 => SampleFourBit(part, vram, rowShift),
            1 => SampleEightBit(part, vram, rowShift),
            _ => throw new FormatException("unsupported sprite texture mode"),
        };
    }

    private static ushort[] SampleFourBit(SpritePart part, ObjectVram vram, int rowShift)
    {
        int page = part.U / FourBitPageTexels;
        int localU = part.U % FourBitPageTexels;
        if (page >= vram.Pages.Count || localU + part.Width > FourBitPageTexels)
            throw new FormatException("4-bit sprite references unavailable VRAM page");
        int paletteRow = part.ClutY + rowShift;
        int paletteX = VramPaletteOriginX + part.ClutX;
        int paletteWord = part.ClutX % ObjectTextureArchiveDecoder.PageWords;
        if (paletteRow < 0 || VramPaletteOriginY + paletteRow >= VramHeight ||
            paletteX + FourBitPaletteSize > VramPaletteOriginX + vram.Pages.Count * ObjectTextureArchiveDecoder.PageWords ||
            paletteWord + FourBitPaletteSize > ObjectTextureArchiveDecoder.PageWords)
            throw new FormatException("4-bit sprite palette outside loaded VRAM");
        ushort[] palettePage = vram.Pages[part.ClutX / ObjectTextureArchiveDecoder.PageWords];
        ReadOnlySpan<ushort> palette = palettePage.AsSpan(paletteRow * ObjectTextureArchiveDecoder.PageWords + paletteWord, FourBitPaletteSize);

        ushort[] texels = vram.Pages[page];
        var words = new ushort[part.Width * part.Height];
        for (int row = 0; row < part.Height; row++)
            for (int column = 0; column < part.Width; column++)
            {
                int u = localU + column;
                ushort packed = texels[(part.V + row) * ObjectTextureArchiveDecoder.PageWords + u / 4];
                words[row * part.Width + column] = palette[(packed >> ((u & 3) * 4)) & 15];
            }

        return words;
    }

    private static ushort[] SampleEightBit(SpritePart part, ObjectVram vram, int rowShift)
    {
        int page = part.U / EightBitPageTexels;
        int localU = part.U % EightBitPageTexels;
        int paletteRow = part.ClutY + rowShift - VramPaletteOriginY;
        if (page >= vram.Pages.Count || localU + part.Width > EightBitPageTexels || paletteRow < 0 ||
            paletteRow >= vram.AlternatePalettes.Count || part.ClutX + 256 > vram.AlternatePalettes[paletteRow].Length)
            throw new FormatException("8-bit sprite bounds or alternate CLUT invalid");
        ushort[] palette = vram.AlternatePalettes[paletteRow];

        ushort[] texels = vram.Pages[page];
        var words = new ushort[part.Width * part.Height];
        for (int row = 0; row < part.Height; row++)
            for (int column = 0; column < part.Width; column++)
            {
                int u = localU + column;
                ushort packed = texels[(part.V + row) * ObjectTextureArchiveDecoder.PageWords + u / 2];
                words[row * part.Width + column] = palette[part.ClutX + ((packed >> (8 * (u & 1))) & 0xFF)];
            }

        return words;
    }
}
