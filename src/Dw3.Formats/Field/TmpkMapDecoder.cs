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
    /// <param name="archive">Raw bytes of the TMPK archive file.</param>
    /// <param name="entryIndex">Zero-based entry index within the archive offset table.</param>
    /// <returns>The decoded TMPK map with its grid dimensions and one-byte cell values.</returns>
    /// <exception cref="FormatException">The entry index is out of range, an offset is malformed, or a component span is truncated.</exception>
    public static TmpkMap DecodeEntry(ReadOnlySpan<byte> archive, int entryIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryIndex);
        int tableOffset = checked(entryIndex * sizeof(uint));
        if (tableOffset + sizeof(uint) > archive.Length)
        {
            throw new FormatException("TMPK archive entry table is truncated.");
        }

        int entryOffset = ReadOffset(archive, tableOffset, "entry");
        if (entryOffset == 0)
        {
            throw new FormatException("TMPK archive entry is absent.");
        }

        if (entryOffset + EntryHeaderBytes > archive.Length)
        {
            throw new FormatException("TMPK archive entry header is truncated.");
        }

        ReadOnlySpan<byte> entry = archive[entryOffset..];
        int[] offsets = ReadComponentOffsets(entry);
        int[] ends = ComputeComponentEnds(entry, offsets);
        return BuildMap(entry, offsets, ends);
    }

    /// <summary>Reads all six component offsets from the entry header and verifies their bounds.</summary>
    /// <param name="entry">Raw entry bytes starting at the six-offset header.</param>
    /// <returns>Array of six valid byte offsets into the entry.</returns>
    private static int[] ReadComponentOffsets(ReadOnlySpan<byte> entry)
    {
        int[] offsets = new int[ComponentCount];
        for (int index = 0; index < ComponentCount; index++)
        {
            offsets[index] = ReadOffset(entry, index * sizeof(uint), "component");
            if (offsets[index] < EntryHeaderBytes || offsets[index] >= entry.Length)
            {
                throw new FormatException("TMPK component offset is outside its entry.");
            }
        }

        if (offsets.Distinct().Count() != ComponentCount)
        {
            throw new FormatException("TMPK component offsets overlap.");
        }

        return offsets;
    }

    /// <summary>Computes the exclusive end boundary for each component by finding the nearest following offset.</summary>
    /// <param name="entry">Raw entry bytes.</param>
    /// <param name="offsets">Array of component start offsets.</param>
    /// <returns>Array of exclusive end offsets corresponding to each component.</returns>
    private static int[] ComputeComponentEnds(ReadOnlySpan<byte> entry, int[] offsets)
    {
        int[] ends = new int[ComponentCount];
        for (int index = 0; index < ComponentCount; index++)
        {
            ends[index] = entry.Length;
            for (int other = 0; other < ComponentCount; other++)
            {
                if (offsets[other] > offsets[index])
                {
                    ends[index] = Math.Min(ends[index], offsets[other]);
                }
            }
        }

        return ends;
    }

    /// <summary>Builds the full cell map from entry components and grid dimensions.</summary>
    /// <param name="entry">Raw entry bytes.</param>
    /// <param name="offsets">Component start offsets within the entry.</param>
    /// <param name="ends">Exclusive component end offsets.</param>
    /// <returns>A TmpkMap with one byte per pixel at the full tile resolution.</returns>
    private static TmpkMap BuildMap(ReadOnlySpan<byte> entry, int[] offsets, int[] ends)
    {
        ReadOnlySpan<byte> grid = Component(entry, offsets, ends, 0);
        if (grid.Length < 2)
        {
            throw new FormatException("TMPK grid component is truncated.");
        }

        int tilesWide = grid[0];
        int tilesHigh = grid[1];
        if (tilesWide == 0 || tilesHigh == 0)
        {
            throw new FormatException("TMPK grid dimensions must be positive.");
        }

        int gridLength = checked(tilesWide * tilesHigh);
        if (grid.Length < 2 + gridLength)
        {
            throw new FormatException("TMPK grid component does not contain all tile indices.");
        }

        int width = checked(tilesWide * 128);
        int height = checked(tilesHigh * 128);
        var cells = new byte[checked(width * height)];
        ReadOnlySpan<byte> gridIndices = grid.Slice(2, gridLength);
        var tables = new TmpkResolveTables(
            gridIndices,
            tilesWide,
            Component(entry, offsets, ends, 1),
            Component(entry, offsets, ends, 2),
            Component(entry, offsets, ends, 3),
            Component(entry, offsets, ends, 4),
            Component(entry, offsets, ends, 5));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                cells[y * width + x] = ResolveCell(tables, x, y);
            }
        }

        return new TmpkMap(width, height, cells);
    }

    /// <summary>Returns the component span at the given index using precomputed offset and end arrays.</summary>
    /// <param name="entry">Raw entry bytes.</param>
    /// <param name="offsets">Component start offsets.</param>
    /// <param name="ends">Exclusive component end offsets.</param>
    /// <param name="index">Zero-based component index (0 = grid, 1–4 = indirection tables, 5 = pixels).</param>
    /// <returns>A slice of the entry covering exactly that component's bytes.</returns>
    private static ReadOnlySpan<byte> Component(ReadOnlySpan<byte> entry, int[] offsets, int[] ends, int index) =>
        entry[offsets[index]..ends[index]];

    /// <summary>Resolves the cell value at pixel coordinates (x, y) using the five-level indirection tables.</summary>
    /// <param name="t">Bundled component spans and tile-grid width.</param>
    /// <param name="x">Pixel x coordinate (0-based, left-to-right).</param>
    /// <param name="y">Pixel y coordinate (0-based, top-to-bottom).</param>
    /// <returns>The one-byte cell value at (x, y).</returns>
    private static byte ResolveCell(TmpkResolveTables t, int x, int y)
    {
        int index = t.Grid[y / 128 * t.TilesWide + x / 128] * 4;
        if ((y & 0x40) != 0)
        {
            index += 2;
        }

        index = ReadByte(t.Cells64, index + ((x & 0x40) != 0 ? 1 : 0), "64-cell table") * 4;
        if ((y & 0x20) != 0)
        {
            index += 2;
        }

        index = ReadUInt16(t.Cells32, index + ((x & 0x20) != 0 ? 1 : 0), "32-cell table") * 4;
        if ((y & 0x10) != 0)
        {
            index += 2;
        }

        index = ReadUInt16(t.Cells16, index + ((x & 0x10) != 0 ? 1 : 0), "16-cell table") * 4;
        if ((y & 8) != 0)
        {
            index += 2;
        }

        index = ReadUInt16(t.Cells8, index + ((x & 8) != 0 ? 1 : 0), "8-cell table");
        int pixelOffset = checked(index * 64 + (y & 7) * 8 + (x & 7));
        return ReadByte(t.Pixels, pixelOffset, "cell pixel blocks");
    }

    /// <summary>Reads a u32 little-endian offset from bytes and validates it fits within int range.</summary>
    /// <param name="bytes">Byte span to read from.</param>
    /// <param name="offset">Byte position of the u32 offset word.</param>
    /// <param name="label">Name used in format exceptions for this offset field.</param>
    /// <returns>The u32 value cast to a non-negative int.</returns>
    private static int ReadOffset(ReadOnlySpan<byte> bytes, int offset, string label)
    {
        if (offset < 0 || offset + sizeof(uint) > bytes.Length)
        {
            throw new FormatException($"TMPK {label} offset is truncated.");
        }

        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));
        if (value > int.MaxValue)
        {
            throw new FormatException($"TMPK {label} offset exceeds supported size.");
        }

        return (int)value;
    }

    /// <summary>Reads a u16 little-endian value from a byte-indexed element in the table.</summary>
    /// <param name="bytes">Byte span containing the table.</param>
    /// <param name="index">Zero-based element index (byte offset = index × 2).</param>
    /// <param name="label">Name used in format exceptions for this table.</param>
    /// <returns>The u16 value at the given element index.</returns>
    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int index, string label)
    {
        int offset = checked(index * sizeof(ushort));
        if (offset < 0 || offset + sizeof(ushort) > bytes.Length)
        {
            throw new FormatException($"TMPK {label} is truncated.");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, sizeof(ushort)));
    }

    /// <summary>Reads a single byte at the given offset, throwing on out-of-range access.</summary>
    /// <param name="bytes">Byte span to read from.</param>
    /// <param name="offset">Byte offset within the span.</param>
    /// <param name="label">Name used in format exceptions for this byte location.</param>
    /// <returns>The byte at the given offset.</returns>
    private static byte ReadByte(ReadOnlySpan<byte> bytes, int offset, string label)
    {
        if (offset < 0 || offset >= bytes.Length)
        {
            throw new FormatException($"TMPK {label} is truncated.");
        }

        return bytes[offset];
    }

    /// <summary>The six read-only component spans needed to resolve a cell, bundled to avoid a wide parameter list.</summary>
    private readonly ref struct TmpkResolveTables
    {
        /// <summary>Grid component: one byte per tile giving the 64×64 cell index for each tile position.</summary>
        public readonly ReadOnlySpan<byte> Grid;

        /// <summary>Number of tiles per row, used to convert a 2D tile position to a grid index.</summary>
        public readonly int TilesWide;

        /// <summary>64×64-cell component: each byte indexes into Cells32.</summary>
        public readonly ReadOnlySpan<byte> Cells64;

        /// <summary>32×32-cell component: each byte indexes into Cells16.</summary>
        public readonly ReadOnlySpan<byte> Cells32;

        /// <summary>16×16-cell component: each byte indexes into Cells8.</summary>
        public readonly ReadOnlySpan<byte> Cells16;

        /// <summary>8×8-cell component: each byte indexes into Pixels.</summary>
        public readonly ReadOnlySpan<byte> Cells8;

        /// <summary>Pixel component: each byte is the final 1×1 cell value written to the output map.</summary>
        public readonly ReadOnlySpan<byte> Pixels;

        /// <summary>Initializes a new instance of the <see cref="TmpkResolveTables"/> struct with all six components.</summary>
        /// <param name="grid">Grid component bytes.</param>
        /// <param name="tilesWide">Number of tiles per row.</param>
        /// <param name="cells64">64×64-cell component bytes.</param>
        /// <param name="cells32">32×32-cell component bytes.</param>
        /// <param name="cells16">16×16-cell component bytes.</param>
        /// <param name="cells8">8×8-cell component bytes.</param>
        /// <param name="pixels">Pixel component bytes.</param>
        public TmpkResolveTables(
            ReadOnlySpan<byte> grid,
            int tilesWide,
            ReadOnlySpan<byte> cells64,
            ReadOnlySpan<byte> cells32,
            ReadOnlySpan<byte> cells16,
            ReadOnlySpan<byte> cells8,
            ReadOnlySpan<byte> pixels)
        {
            Grid = grid;
            TilesWide = tilesWide;
            Cells64 = cells64;
            Cells32 = cells32;
            Cells16 = cells16;
            Cells8 = cells8;
            Pixels = pixels;
        }
    }
}
