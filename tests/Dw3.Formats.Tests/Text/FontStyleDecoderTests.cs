using System.Buffers.Binary;
using Dw3.Formats.Text;
using Xunit;

namespace Dw3.Formats.Tests.Text;

/// <summary>Synthetic (disc-free) tests for <see cref="FontStyleDecoder"/>.</summary>
public sealed class FontStyleDecoderTests
{
    // Minimal SLES_039.36 stub large enough to contain the TextStyle table and one glyph entry.
    // TextStyleTableOffset = 0x3E574; descriptor for style 1 starts at 0x3E574 + 24 = 0x3E58C.
    // We build a minimal buffer just large enough, with three valid descriptors.
    private const int TableOffset = 0x3E574;

    private const int DescSize = 24;

    // Need 4 descriptors (0..3) + glyph/icon tables after them.
    private const int MinBufferBeforeData = TableOffset + (4 * DescSize);

    // For each style, 230 glyphs * 11 bytes + 114 icons * 11 bytes = 3784 bytes.
    private const int GlyphCount = 230;

    private const int IconCount = 114;

    private const int RecordSize = 11;

    private const int StyleDataSize = (GlyphCount * RecordSize) + (IconCount * RecordSize);

    // Load address of SLES_039.36.
    private const uint LoadAddr = 0x80010000u;

    private const int ExeHeader = 0x800;

    private static int TotalSize => MinBufferBeforeData + (3 * StyleDataSize);

    [Fact]
    public void Decode_ValidExecutable_ReturnsThreeStyles()
    {
        var exe = BuildValidExecutable();
        var styles = FontStyleDecoder.Decode(exe);
        Assert.Equal(3, styles.Count);
    }

    [Fact]
    public void Decode_LineHeightsAre14_11_9()
    {
        var exe = BuildValidExecutable();
        var styles = FontStyleDecoder.Decode(exe);
        Assert.Equal(14, styles[0].LineHeight);
        Assert.Equal(11, styles[1].LineHeight);
        Assert.Equal(9, styles[2].LineHeight);
    }

    [Fact]
    public void Decode_GlyphAndIconCountsAreCorrect()
    {
        var exe = BuildValidExecutable();
        var styles = FontStyleDecoder.Decode(exe);
        foreach (var s in styles)
        {
            Assert.Equal(GlyphCount, s.Glyphs.Count);
            Assert.Equal(IconCount, s.Icons.Count);
        }
    }

    [Fact]
    public void Decode_GlyphMetricFields_AreReadCorrectly()
    {
        var exe = BuildValidExecutable();
        var styles = FontStyleDecoder.Decode(exe);

        // Style 1 (index 0): page = 1, dx = -1, advance varies by j
        var s1 = styles[0];
        Assert.Equal(1, s1.Glyphs[0].Page);
        Assert.Equal(-1, s1.Glyphs[0].Dx);
        Assert.Equal(0, s1.Glyphs[0].Advance);
        Assert.Equal(1, (int)s1.Glyphs[1].Advance);
    }

    [Fact]
    public void Decode_ExecutableTooShort_Throws()
    {
        var exe = new byte[0x3E574]; // too short to hold all 4 descriptors
        Assert.Throws<FormatException>(() => FontStyleDecoder.Decode(exe));
    }

    [Fact]
    public void Decode_WrongLineHeight_Throws()
    {
        var exe = BuildValidExecutable();

        // Corrupt style 1 line height at descriptor index 1.
        int descOffset = TableOffset + (1 * DescSize);
        exe[descOffset + 1] = 20; // wrong
        Assert.Throws<FormatException>(() => FontStyleDecoder.Decode(exe));
    }

    [Fact]
    public void Decode_WrongGlyphCount_Throws()
    {
        var exe = BuildValidExecutable();
        int descOffset = TableOffset + (1 * DescSize);
        BinaryPrimitives.WriteInt16LittleEndian(exe.AsSpan(descOffset + 20), 100); // less than 230, should throw
        Assert.Throws<FormatException>(() => FontStyleDecoder.Decode(exe));
    }

    [Fact]
    public void Decode_GlyphPointerBelowLoadAddress_Throws()
    {
        var exe = BuildValidExecutable();
        int descOffset = TableOffset + (1 * DescSize);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(descOffset + 4), 0x00001234u); // below load addr
        Assert.Throws<FormatException>(() => FontStyleDecoder.Decode(exe));
    }

    [Fact]
    public void Decode_GlyphTableOutOfBounds_Throws()
    {
        var exe = BuildValidExecutable();

        // Set glyph pointer to point past the end of the buffer.
        int descOffset = TableOffset + (1 * DescSize);
        uint outOfRange = LoadAddr - ExeHeader + (uint)(TotalSize - 5);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(descOffset + 4), outOfRange);
        Assert.Throws<FormatException>(() => FontStyleDecoder.Decode(exe));
    }

    // Place each style's data right after the table.
    private static int GlyphTableOffset(int styleIndex) => MinBufferBeforeData + (styleIndex * StyleDataSize);

    private static int IconTableOffset(int styleIndex) => GlyphTableOffset(styleIndex) + (GlyphCount * RecordSize);

    private static byte[] BuildValidExecutable()
    {
        var buf = new byte[TotalSize];
        int[] lineHeights = [14, 11, 9];
        for (int i = 0; i < 3; i++)
        {
            int styleNumber = i + 1; // descriptor at index (i+1)
            int descOffset = TableOffset + (styleNumber * DescSize);

            // blend = 0xFF, line_height = lineHeights[i]
            buf[descOffset] = 0xFF;
            buf[descOffset + 1] = (byte)(sbyte)lineHeights[i];

            // glyphs_ptr: VA for GlyphTableOffset(i)
            uint glyphVa = LoadAddr - ExeHeader + (uint)GlyphTableOffset(i);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(descOffset + 4), glyphVa);

            // icons_ptr
            uint iconVa = LoadAddr - ExeHeader + (uint)IconTableOffset(i);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(descOffset + 8), iconVa);

            // glyph_count = 230, icon_count = 114
            BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(descOffset + 20), (short)GlyphCount);
            BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(descOffset + 22), (short)IconCount);

            // Fill glyph data: glyph j has advance = (byte)(j & 0xFF), dx = (sbyte)(-1)
            for (int j = 0; j < GlyphCount; j++)
            {
                int pos = GlyphTableOffset(i) + (j * RecordSize);
                buf[pos] = (byte)styleNumber; // page
                buf[pos + 7] = 0xFF; // dx = -1 (sbyte)
                buf[pos + 9] = (byte)(j & 0xFF); // advance
            }

            // Fill icon data: icon j has advance = 0xA0 + j
            for (int j = 0; j < IconCount; j++)
            {
                int pos = IconTableOffset(i) + (j * RecordSize);
                buf[pos + 9] = (byte)((0xA0 + j) & 0xFF); // advance
            }
        }

        return buf;
    }
}
