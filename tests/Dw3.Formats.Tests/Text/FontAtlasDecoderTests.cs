using System.Buffers.Binary;
using Dw3.Formats.Text;
using Xunit;

namespace Dw3.Formats.Tests.Text;

/// <summary>Synthetic (disc-free) tests for <see cref="FontAtlasDecoder"/>.</summary>
public sealed class FontAtlasDecoderTests
{
    // Build a minimal valid CMFONTTM.BIN with two pages.
    // The file begins with a 4-byte FILE_CACHE header (skipped).
    // Then the archive body: zero-terminated u32 offset table, followed by RLEN-compressed TIM pages.
    // Each TIM is a minimal 4-bit TIM (magic 0x10, flags 0x02) with a 2×2 image.
    // The CLUT must sit at the right position for page 0 to avoid the "too small" error.
    // We use 256×256 pages (words=64, height=256) so the CLUT fits.

    private const int PageWidth = 256;
    private const int PageHeight = 256;
    private const int WordsPerLine = PageWidth / 4; // 64 words = 128 bytes per row
    private const int PixelBytes = WordsPerLine * PageHeight * 2; // 64 * 256 * 2 = 32768

    private static byte[] BuildRlenPage(byte[] pixels)
    {
        // Build a raw TIM then wrap it in RLEN.
        // TIM: magic(4) flags(4) imageBlock.length(4) vram_x(2) vram_y(2) words(2) height(2) pixels
        var tim = new byte[8 + 12 + pixels.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(tim.AsSpan(0), 0x10u);  // magic
        BinaryPrimitives.WriteUInt32LittleEndian(tim.AsSpan(4), 0x02u);  // flags
        BinaryPrimitives.WriteUInt32LittleEndian(tim.AsSpan(8), (uint)(12 + pixels.Length)); // blockLen
        // vram_x, vram_y at [12..15] = 0
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(16), (ushort)WordsPerLine); // words
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(18), (ushort)PageHeight);   // height
        pixels.CopyTo(tim, 20);

        // Wrap in RLEN as a raw literal stream (just literal copy of all bytes).
        int outputSize = tim.Length;
        // RLEN header: "RLEN" + u32 outputSize
        // Commands: for each chunk of up to 127 bytes, emit literal run cmd (1..127) followed by bytes.
        var rlen = new List<byte>();
        rlen.Add((byte)'R'); rlen.Add((byte)'L'); rlen.Add((byte)'E'); rlen.Add((byte)'N');
        rlen.Add((byte)(outputSize & 0xFF)); rlen.Add((byte)((outputSize >> 8) & 0xFF));
        rlen.Add((byte)((outputSize >> 16) & 0xFF)); rlen.Add((byte)((outputSize >> 24) & 0xFF));
        int pos = 0;
        while (pos < tim.Length)
        {
            int chunk = Math.Min(127, tim.Length - pos);
            rlen.Add((byte)chunk);
            rlen.AddRange(tim[pos..(pos + chunk)]);
            pos += chunk;
        }
        rlen.Add(0x00); // RLEN terminator
        return [.. rlen];
    }

    private static byte[] BuildValidArchive()
    {
        var pixels = new byte[PixelBytes];
        // Place a known CLUT value at palette row 2, col 1 in the pixel-as-halfwords space.
        // ClutColumnX=48, ClutRowY=232. Halfword at (row*wordsPerLine + col) * 2 where col is within 48..63.
        // Put a non-zero word (non-transparent) at row 232, col 48.
        int bytePos = (232 * WordsPerLine + 48) * 2;
        // Write a non-zero 15-bit BGR word (e.g., pure red 0x001F) for palette entry 0 of row 0.
        BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(bytePos), 0x001F); // entry 0 of row 0

        byte[] page0 = BuildRlenPage(pixels);
        byte[] page1 = BuildRlenPage(new byte[PixelBytes]);

        // Archive body: offset table (two entries + zero terminator), then page data.
        // Offset is relative to archive body start (=bodyBase=4 in the file).
        int tableSize = 3 * 4; // two non-zero offsets + terminator
        int offset0 = tableSize;              // page0 immediately after table
        int offset1 = tableSize + page0.Length; // page1 after page0

        var file = new List<byte>();
        // FILE_CACHE header (4 bytes, ignored)
        file.AddRange([0, 0, 0, 0]);
        // Offset table (relative to bodyBase=4)
        void WriteU32(int v) { file.Add((byte)(v & 0xFF)); file.Add((byte)((v >> 8) & 0xFF)); file.Add((byte)((v >> 16) & 0xFF)); file.Add((byte)((v >> 24) & 0xFF)); }
        WriteU32(offset0);
        WriteU32(offset1);
        WriteU32(0); // terminator
        file.AddRange(page0);
        file.AddRange(page1);
        return [.. file];
    }

    [Fact]
    public void Decode_ValidArchive_ReturnsTwoPages()
    {
        var archive = BuildValidArchive();
        var atlas = FontAtlasDecoder.Decode(archive, "deadbeef");
        Assert.Equal(2, atlas.Pages.Count);
    }

    [Fact]
    public void Decode_ValidArchive_PageDimensionsCorrect()
    {
        var archive = BuildValidArchive();
        var atlas = FontAtlasDecoder.Decode(archive, "deadbeef");
        Assert.Equal(PageWidth, atlas.Pages[0].Width);
        Assert.Equal(PageHeight, atlas.Pages[0].Height);
    }

    [Fact]
    public void Decode_ValidArchive_EightPalettes()
    {
        var archive = BuildValidArchive();
        var atlas = FontAtlasDecoder.Decode(archive, "deadbeef");
        Assert.Equal(8, atlas.Palettes.Count);
        foreach (var row in atlas.Palettes)
            Assert.Equal(16, row.Count);
    }

    [Fact]
    public void Decode_Sha256_IsPassedThrough()
    {
        var archive = BuildValidArchive();
        var atlas = FontAtlasDecoder.Decode(archive, "testsha");
        Assert.Equal("testsha", atlas.SourceSha256);
    }

    [Fact]
    public void HalfwordToRgba_Zero_IsTransparent()
    {
        var (_, _, _, a) = FontAtlasDecoder.HalfwordToRgba(0);
        Assert.Equal(0, a);
    }

    [Fact]
    public void HalfwordToRgba_PureRed_MapsCorrectly()
    {
        // 15-bit BGR: R=31, G=0, B=0 → word = 0x001F
        var (r, g, b, a) = FontAtlasDecoder.HalfwordToRgba(0x001F);
        Assert.Equal(255, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
        Assert.Equal(255, a);
    }

    [Fact]
    public void Decode_TooFewPages_Throws()
    {
        // Archive with only 1 page.
        byte[] page0 = BuildRlenPage(new byte[PixelBytes]);
        var file = new List<byte> { 0, 0, 0, 0 }; // FILE_CACHE header
        void WriteU32(int v) { file.Add((byte)(v & 0xFF)); file.Add((byte)((v >> 8) & 0xFF)); file.Add((byte)((v >> 16) & 0xFF)); file.Add((byte)((v >> 24) & 0xFF)); }
        int tableSize = 2 * 4; // one entry + terminator
        WriteU32(tableSize); // offset to page0
        WriteU32(0);         // terminator
        file.AddRange(page0);
        Assert.Throws<FormatException>(() => FontAtlasDecoder.Decode([.. file], "x"));
    }

    [Fact]
    public void Decode_TruncatedFile_Throws()
    {
        Assert.Throws<FormatException>(() => FontAtlasDecoder.Decode([0, 0, 0], "x"));
    }

    [Fact]
    public void RenderPage_TransparentEntry_IsZeroAlpha()
    {
        // Build a page with all zero pixels (all index 0).
        var page = new FontAtlasPage(4, 1, [0x00, 0x00]); // 4 pixels, all index 0
        var palette = new (byte R, byte G, byte B, byte A)[16];
        palette[0] = (0, 0, 0, 0); // transparent

        var rgba = FontAtlasDecoder.RenderPage(page, palette);
        Assert.Equal(4 * 4, rgba.Length);
        for (int i = 0; i < rgba.Length; i += 4)
            Assert.Equal(0, rgba[i + 3]); // alpha 0
    }
}
