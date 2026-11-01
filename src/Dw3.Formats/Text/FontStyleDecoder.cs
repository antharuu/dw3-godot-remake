using System.Buffers.Binary;

namespace Dw3.Formats.Text;

/// <summary>
/// Reads the three active text-rendering styles from the game executable (SLES_039.36).
///
/// The TextStyle[4] descriptor table sits at SLES file offset 0x3E574
/// (virtual address 0x8004DD74 on the EU disc). Indices 1–3 hold the active styles;
/// index 0 is unused. Each descriptor is 24 bytes:
/// <code>
///   [0]     u8  blend
///   [1]     s8  line_height
///   [2..3]  u16 padding
///   [4..7]  u32 glyphs_ptr     (virtual address of Glyph[glyph_count])
///   [8..11] u32 icons_ptr      (virtual address of Glyph[icon_count])
///   [12..15] u32 glyph_map_ptr
///   [16..19] u32 icon_map_ptr
///   [20..21] s16 glyph_count
///   [22..23] s16 icon_count
/// </code>
/// Each on-disc Glyph record is 11 bytes (page, u, v, clutX, clutY, width, height,
/// s8 dx, s8 dy, advance, unused); the unused trailing byte is discarded.
/// Validated against the EU disc: 230 glyphs and 114 icons per style,
/// line heights 14, 11, 9.
/// </summary>
public static class FontStyleDecoder
{
    // EU disc (SLES-03936): SLES_039.36 PS-X EXE header and load address.
    // Virtual address → SLES file offset: 0x800 + (va − 0x80010000).
    private const int ExecutableHeaderSize = 0x800;
    private const uint ExecutableLoadAddress = 0x80010000u;

    // TextStyle[4] table: SLES offset 0x3E574, VA 0x8004DD74.
    private const int TextStyleTableOffset = 0x3E574;
    private const int TextStyleDescriptorSize = 24;

    // The three active styles are at descriptor indices 1, 2, 3.
    private const int FirstActiveStyleIndex = 1;
    private const int ActiveStyleCount = 3;

    // Each on-disc Glyph record is 11 bytes; last byte (unused) is discarded.
    private const int GlyphRecordSize = 11;

    // Disc-verified per-style constants (EU SLES-03936).
    private static readonly int[] ExpectedLineHeights = [14, 11, 9];
    private const int ExpectedGlyphCount = 230;
    private const int ExpectedIconCount = 114;

    /// <summary>
    /// Decodes styles 1, 2, and 3 from the given executable bytes.
    /// Returns a three-element list in style order (index 0 = style 1, etc.).
    /// </summary>
    /// <param name="executable">Full contents of SLES_039.36.</param>
    /// <returns>Metrics for the three active descriptor slots in ascending style order.</returns>
    /// <exception cref="FormatException">
    /// Any of: table or table entry is outside the buffer; line height, glyph count, or icon count
    /// does not match the disc-verified expectation; a metrics pointer is out of range.
    /// </exception>
    public static IReadOnlyList<FontStyleMetrics> Decode(ReadOnlySpan<byte> executable)
    {
        int tableEnd = TextStyleTableOffset
            + (FirstActiveStyleIndex + ActiveStyleCount) * TextStyleDescriptorSize;
        if (executable.Length < tableEnd)
        {
            throw new FormatException(
                $"Executable ({executable.Length} bytes) is too short to contain the TextStyle table (needs {tableEnd} bytes).");
        }

        var styles = new FontStyleMetrics[ActiveStyleCount];
        for (int i = 0; i < ActiveStyleCount; i++)
        {
            int descriptorOffset = TextStyleTableOffset + (FirstActiveStyleIndex + i) * TextStyleDescriptorSize;
            styles[i] = DecodeStyle(executable, descriptorOffset, i + 1);
        }
        return styles;
    }

    /// <summary>Decodes one 24-byte TextStyle descriptor and its profile-bounded metric tables.</summary>
    /// <param name="exe">Full executable bytes used to resolve descriptor pointers.</param>
    /// <param name="descOffset">File offset of the 24-byte descriptor.</param>
    /// <param name="styleNumber">One-based active style number used for profile validation.</param>
    /// <returns>Metrics read from the descriptor's glyph and icon virtual addresses.</returns>
    private static FontStyleMetrics DecodeStyle(ReadOnlySpan<byte> exe, int descOffset, int styleNumber)
    {
        sbyte lineHeight = (sbyte)exe[descOffset + 1];
        uint glyphsVa = BinaryPrimitives.ReadUInt32LittleEndian(exe[(descOffset + 4)..]);
        uint iconsVa = BinaryPrimitives.ReadUInt32LittleEndian(exe[(descOffset + 8)..]);
        short glyphCount = BinaryPrimitives.ReadInt16LittleEndian(exe[(descOffset + 20)..]);
        short iconCount = BinaryPrimitives.ReadInt16LittleEndian(exe[(descOffset + 22)..]);

        int expectedLh = ExpectedLineHeights[styleNumber - 1];
        if (lineHeight != expectedLh)
        {
            throw new FormatException(
                $"Style {styleNumber} line height {lineHeight} does not match expected {expectedLh}.");
        }

        if (glyphCount < ExpectedGlyphCount)
        {
            throw new FormatException(
                $"Style {styleNumber} glyph count {glyphCount} is less than the EU profile minimum {ExpectedGlyphCount}.");
        }

        if (iconCount != ExpectedIconCount)
        {
            throw new FormatException(
                $"Style {styleNumber} icon count {iconCount} does not match expected {ExpectedIconCount}.");
        }

        int glyphsOffset = VaToOffset(glyphsVa, styleNumber, "glyphs");
        int iconsOffset = VaToOffset(iconsVa, styleNumber, "icons");

        // Always read the profile-defined count (230/114) even if the descriptor declares more.
        // The descriptor on the EU disc declares 234 glyph entries, but only 230 are mapped to
        // active token codes (4..233). The last few descriptor entries are unmapped.
        RequireInBounds(exe.Length, glyphsOffset, ExpectedGlyphCount * GlyphRecordSize, $"style {styleNumber} glyph table");
        RequireInBounds(exe.Length, iconsOffset, ExpectedIconCount * GlyphRecordSize, $"style {styleNumber} icon table");

        return new FontStyleMetrics(
            LineHeight: lineHeight,
            Glyphs: ReadMetrics(exe, glyphsOffset, ExpectedGlyphCount),
            Icons: ReadMetrics(exe, iconsOffset, ExpectedIconCount));
    }

    /// <summary>Reads consecutive 11-byte Glyph records while discarding each record's unused final byte.</summary>
    /// <param name="exe">Executable bytes containing the metric records.</param>
    /// <param name="offset">File offset of the first 11-byte record.</param>
    /// <param name="count">Number of profile-validated records to read.</param>
    /// <returns>The decoded metrics in source-record order.</returns>
    private static GlyphMetric[] ReadMetrics(ReadOnlySpan<byte> exe, int offset, int count)
    {
        var metrics = new GlyphMetric[count];
        for (int i = 0; i < count; i++)
        {
            int pos = offset + i * GlyphRecordSize;
            metrics[i] = new GlyphMetric(
                Page: exe[pos],
                U: exe[pos + 1],
                V: exe[pos + 2],
                ClutX: exe[pos + 3],
                ClutY: exe[pos + 4],
                Width: exe[pos + 5],
                Height: exe[pos + 6],
                Dx: (sbyte)exe[pos + 7],
                Dy: (sbyte)exe[pos + 8],
                // pos+10 (unused) discarded
                Advance: exe[pos + 9]);
        }
        return metrics;
    }

    /// <summary>Converts a PS-X EXE virtual address into a file offset after the 0x800-byte header.</summary>
    /// <param name="va">Virtual address stored in the TextStyle descriptor.</param>
    /// <param name="styleNumber">One-based style number for diagnostic context.</param>
    /// <param name="table">Glyph or icon table label for diagnostic context.</param>
    /// <returns>The representable file offset corresponding to <paramref name="va"/>.</returns>
    private static int VaToOffset(uint va, int styleNumber, string table)
    {
        if (va < ExecutableLoadAddress)
        {
            throw new FormatException(
                $"Style {styleNumber} {table} pointer 0x{va:X8} is below the executable load address 0x{ExecutableLoadAddress:X8}.");
        }

        long fileOffset = ExecutableHeaderSize + (long)(va - ExecutableLoadAddress);
        if (fileOffset > int.MaxValue)
        {
            throw new FormatException(
                $"Style {styleNumber} {table} pointer 0x{va:X8} maps to a file offset that exceeds int range.");
        }

        return (int)fileOffset;
    }

    /// <summary>Validates that a fixed-size executable region is entirely inside the input buffer.</summary>
    /// <param name="bufferLen">Total executable byte count.</param>
    /// <param name="offset">First byte offset of the required region.</param>
    /// <param name="size">Required byte count.</param>
    /// <param name="label">Human-readable table label used in the format error.</param>
    private static void RequireInBounds(int bufferLen, int offset, int size, string label)
    {
        if (offset < 0 || (long)offset + size > bufferLen)
        {
            throw new FormatException(
                $"The {label} (offset=0x{offset:X}, size={size}) is outside the executable ({bufferLen} bytes).");
        }
    }
}
