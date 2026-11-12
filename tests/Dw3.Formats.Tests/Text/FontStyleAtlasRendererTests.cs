using System.Buffers.Binary;
using Dw3.Formats.Text;
using Xunit;

namespace Dw3.Formats.Tests.Text;

public sealed class FontStyleAtlasRendererTests
{
    [Fact]
    public void Render_UsesGlyphClutInsteadOfDialoguePalette()
    {
        var bytes = new byte[64 * 240 * 2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(((30 * 64) + 3 + 7) * 2), 0x001F);
        var clut = new FontAtlasPage(256, 240, bytes);
        var glyph = new GlyphMetric(0, 0, 0, 3, 28, 8, 8, 0, 0, 8);
        var pixels = new FontAtlasPage(4, 1, [0x77, 0x77]);

        byte[] rgba = FontStyleAtlasRenderer.Render(pixels, clut, glyph, 2);

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, rgba.Take(4));
        Assert.Equal(16, rgba.Length);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void Render_RejectsInvalidPalette(int palette)
    {
        var page = new FontAtlasPage(256, 240, new byte[64 * 240 * 2]);
        var glyph = new GlyphMetric(0, 0, 0, 3, 28, 8, 8, 0, 0, 8);

        Assert.Throws<ArgumentOutOfRangeException>(() => FontStyleAtlasRenderer.Render(page, page, glyph, palette));
    }

    [Fact]
    public void Render_RejectsTruncatedClutStorage()
    {
        var page = new FontAtlasPage(256, 240, []);
        var glyph = new GlyphMetric(0, 0, 0, 3, 28, 8, 8, 0, 0, 8);

        Assert.Throws<FormatException>(() => FontStyleAtlasRenderer.Render(page, page, glyph, 0));
    }

    [Fact]
    public void Render_RejectsClutBeyondPage()
    {
        var page = new FontAtlasPage(256, 240, new byte[64 * 240 * 2]);
        var glyph = new GlyphMetric(0, 0, 0, 60, 239, 8, 8, 0, 0, 8);

        Assert.Throws<FormatException>(() => FontStyleAtlasRenderer.Render(page, page, glyph, 1));
    }
}
