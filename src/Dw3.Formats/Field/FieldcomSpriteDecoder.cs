using System.Buffers.Binary;
using Dw3.Formats.Binary;
using Dw3.Formats.Water;

namespace Dw3.Formats.Field;

/// <summary>Two-dimensional FIELDCOM VRAM pages, each 64 PS1 words by 256 rows.</summary>
public sealed record FieldcomVram(IReadOnlyList<ushort[]> Pages);

/// <summary>Decodes RLEN-wrapped direct-colour TIM pages embedded by FIELDCOM entries.</summary>
public static class FieldcomVramDecoder
{
    private const int PageWords = 64;
    private const int PageRows = 256;
    private const uint TimMagic = 0x10;
    private const uint DirectColourFlag = 2;

    /// <exception cref="FormatException">An entry is not the expected RLEN direct-colour TIM page.</exception>
    public static FieldcomVram Decode(IReadOnlyList<ReadOnlyMemory<byte>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
            throw new FormatException("FIELDCOM has no VRAM pages");
        var pages = new ushort[entries.Count][];
        for (int index = 0; index < entries.Count; index++)
            pages[index] = DecodePage(entries[index].Span);
        return new FieldcomVram(pages);
    }

    private static ushort[] DecodePage(ReadOnlySpan<byte> entry)
    {
        if (entry.Length < 9 || BinaryPrimitives.ReadUInt32LittleEndian(entry) != 8)
            throw new FormatException("FIELDCOM VRAM entry offset is invalid");
        byte[] data;
        try
        {
            data = RlenDecoder.Decompress(entry[8..]);
        }
        catch (FormatException exception)
        {
            throw new FormatException($"FIELDCOM VRAM RLEN is invalid: {exception.Message}", exception);
        }

        if (data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(data) != TimMagic ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != DirectColourFlag)
            throw new FormatException("FIELDCOM VRAM page TIM header is invalid");
        uint blockSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        ushort words = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(16));
        ushort rows = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(18));
        int pixels = PageWords * PageRows * sizeof(ushort);
        if (blockSize != data.Length - 8 || words != PageWords || rows != PageRows || data.Length != 20 + pixels)
            throw new FormatException("FIELDCOM VRAM page rectangle is invalid");

        var page = new ushort[PageWords * PageRows];
        for (int index = 0; index < page.Length; index++)
            page[index] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(20 + index * sizeof(ushort)));
        return page;
    }
}

/// <summary>One FIELDCOM sprite composed in PS1 word colours.</summary>
public sealed record FieldcomSpriteImage(int Width, int Height, int OffsetX, int OffsetY, int Abr, ushort[] Words);

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
            throw new FormatException("FIELDCOM sprite frame is truncated");
        int count = BinaryPrimitives.ReadInt16LittleEndian(frame);
        int row = BinaryPrimitives.ReadInt16LittleEndian(frame[2..]);
        int abr = BinaryPrimitives.ReadInt16LittleEndian(frame[4..]);
        if (count <= 0 || count > MaximumParts || frame.Length < 6 + count * 6)
            throw new FormatException("FIELDCOM sprite frame part count is invalid");

        var placed = new (int X, int Y, int Width, int Height, ushort[] Words)[count];
        for (int index = 0; index < count; index++)
        {
            int partIndex = BinaryPrimitives.ReadInt16LittleEndian(frame[(6 + index * 6)..]);
            if (partIndex < 0 || partIndex >= parts.Count)
                throw new FormatException("FIELDCOM sprite frame references missing part");
            SpritePart part = parts[partIndex];
            placed[index] = (
                BinaryPrimitives.ReadInt16LittleEndian(frame[(8 + index * 6)..]),
                BinaryPrimitives.ReadInt16LittleEndian(frame[(10 + index * 6)..]),
                part.Width,
                part.Height,
                Sample(part, vram, row));
        }

        int minX = placed.Min(item => item.X);
        int minY = placed.Min(item => item.Y);
        int width = placed.Max(item => item.X + item.Width) - minX;
        int height = placed.Max(item => item.Y + item.Height) - minY;
        var words = new ushort[width * height];
        for (int index = count - 1; index >= 0; index--)
        {
            var part = placed[index];
            for (int y = 0; y < part.Height; y++)
                for (int x = 0; x < part.Width; x++)
                {
                    ushort word = part.Words[y * part.Width + x];
                    if (word != 0)
                        words[(part.Y - minY + y) * width + part.X - minX + x] = word;
                }
        }
        return new FieldcomSpriteImage(width, height, minX, minY, abr, words);
    }

    private static ushort[] Sample(SpritePart part, FieldcomVram vram, int row)
    {
        if (part.Mode != 0 || part.U < 0 || part.V < 0 || part.Width <= 0 || part.Height <= 0 ||
            part.ClutX < 0 || part.ClutY < 0 || part.V + part.Height > 256)
            throw new FormatException("FIELDCOM sprite part bounds are invalid");
        int texturePage = part.U / PageTexels;
        int localU = part.U % PageTexels;
        if (texturePage >= vram.Pages.Count || localU + part.Width > PageTexels)
            throw new FormatException("FIELDCOM 4-bit sprite references unavailable VRAM page");
        int paletteRow = part.ClutY + row;
        if (paletteRow < 0 || paletteRow >= 256 || part.ClutX + 16 > PageWords)
            throw new FormatException("FIELDCOM sprite palette is outside VRAM");

        ushort[] page = vram.Pages[texturePage];
        var result = new ushort[part.Width * part.Height];
        for (int y = 0; y < part.Height; y++)
            for (int x = 0; x < part.Width; x++)
            {
                int u = localU + x;
                ushort packed = page[(part.V + y) * PageWords + u / 4];
                int paletteIndex = (packed >> ((u & 3) * 4)) & 15;
                result[y * part.Width + x] = page[paletteRow * PageWords + part.ClutX + paletteIndex];
            }
        return result;
    }
}
