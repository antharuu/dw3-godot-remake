using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>
/// Decodes a cell map stored in a TMPK archive entry. Each entry owns six relative
/// component offsets: the tile grid, four indirection tables, and eight-by-eight cell blocks.
/// </summary>
public static class TmpkMapDecoder
{
    private const int ComponentCount = 6;
    private const int EntryHeaderBytes = ComponentCount * sizeof(uint);

    /// <summary>Decodes one archive entry into its expanded one-byte cell map.</summary>
    public static TmpkMap DecodeEntry(ReadOnlySpan<byte> archive, int entryIndex)
    {
        if (entryIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(entryIndex));
        int tableOffset = checked(entryIndex * sizeof(uint));
        if (tableOffset + sizeof(uint) > archive.Length)
            throw new FormatException("TMPK archive entry table is truncated.");
        int entryOffset = ReadOffset(archive, tableOffset, "entry");
        if (entryOffset == 0)
            throw new FormatException("TMPK archive entry is absent.");
        if (entryOffset + EntryHeaderBytes > archive.Length)
            throw new FormatException("TMPK archive entry header is truncated.");

        ReadOnlySpan<byte> entry = archive[entryOffset..];
        int[] offsets = new int[ComponentCount];
        for (int index = 0; index < ComponentCount; index++)
        {
            offsets[index] = ReadOffset(entry, index * sizeof(uint), "component");
            if (offsets[index] < EntryHeaderBytes || offsets[index] >= entry.Length)
                throw new FormatException("TMPK component offset is outside its entry.");
        }
        if (offsets.Distinct().Count() != ComponentCount)
            throw new FormatException("TMPK component offsets overlap.");

        int[] ends = new int[ComponentCount];
        for (int index = 0; index < ComponentCount; index++)
        {
            ends[index] = entry.Length;
            for (int other = 0; other < ComponentCount; other++)
                if (offsets[other] > offsets[index]) ends[index] = Math.Min(ends[index], offsets[other]);
        }

        ReadOnlySpan<byte> grid = Component(entry, offsets, ends, 0);
        if (grid.Length < 2)
            throw new FormatException("TMPK grid component is truncated.");
        int tilesWide = grid[0];
        int tilesHigh = grid[1];
        if (tilesWide == 0 || tilesHigh == 0)
            throw new FormatException("TMPK grid dimensions must be positive.");
        int gridLength = checked(tilesWide * tilesHigh);
        if (grid.Length < 2 + gridLength)
            throw new FormatException("TMPK grid component does not contain all tile indices.");

        int width = checked(tilesWide * 128);
        int height = checked(tilesHigh * 128);
        var cells = new byte[checked(width * height)];
        ReadOnlySpan<byte> gridIndices = grid.Slice(2, gridLength);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                cells[y * width + x] = ResolveCell(gridIndices, tilesWide, Component(entry, offsets, ends, 1), Component(entry, offsets, ends, 2), Component(entry, offsets, ends, 3), Component(entry, offsets, ends, 4), Component(entry, offsets, ends, 5), x, y);
        return new TmpkMap(width, height, cells);
    }

    private static ReadOnlySpan<byte> Component(ReadOnlySpan<byte> entry, int[] offsets, int[] ends, int index) => entry[offsets[index]..ends[index]];

    private static byte ResolveCell(ReadOnlySpan<byte> grid, int tilesWide, ReadOnlySpan<byte> cells64, ReadOnlySpan<byte> cells32, ReadOnlySpan<byte> cells16, ReadOnlySpan<byte> cells8, ReadOnlySpan<byte> pixels, int x, int y)
    {
        int index = grid[y / 128 * tilesWide + x / 128] * 4;
        if ((y & 0x40) != 0) index += 2;
        index = ReadByte(cells64, index + ((x & 0x40) != 0 ? 1 : 0), "64-cell table") * 4;
        if ((y & 0x20) != 0) index += 2;
        index = ReadUInt16(cells32, index + ((x & 0x20) != 0 ? 1 : 0), "32-cell table") * 4;
        if ((y & 0x10) != 0) index += 2;
        index = ReadUInt16(cells16, index + ((x & 0x10) != 0 ? 1 : 0), "16-cell table") * 4;
        if ((y & 8) != 0) index += 2;
        index = ReadUInt16(cells8, index + ((x & 8) != 0 ? 1 : 0), "8-cell table");
        int pixelOffset = checked(index * 64 + (y & 7) * 8 + (x & 7));
        return ReadByte(pixels, pixelOffset, "cell pixel blocks");
    }

    private static int ReadOffset(ReadOnlySpan<byte> bytes, int offset, string label)
    {
        if (offset < 0 || offset + sizeof(uint) > bytes.Length)
            throw new FormatException($"TMPK {label} offset is truncated.");
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));
        if (value > int.MaxValue)
            throw new FormatException($"TMPK {label} offset exceeds supported size.");
        return (int)value;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int index, string label)
    {
        int offset = checked(index * sizeof(ushort));
        if (offset < 0 || offset + sizeof(ushort) > bytes.Length)
            throw new FormatException($"TMPK {label} is truncated.");
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, sizeof(ushort)));
    }

    private static byte ReadByte(ReadOnlySpan<byte> bytes, int offset, string label)
    {
        if (offset < 0 || offset >= bytes.Length)
            throw new FormatException($"TMPK {label} is truncated.");
        return bytes[offset];
    }
}

/// <summary>An expanded TMPK cell map.</summary>
public sealed record TmpkMap(int Width, int Height, byte[] Cells);
