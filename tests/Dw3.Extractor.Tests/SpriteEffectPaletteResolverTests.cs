using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class SpriteEffectPaletteResolverTests
{
    private static readonly IReadOnlyList<HudTexturePage> Page = [HudTexturePageDecoder.DecodeFourBitTim(FabricatedSprites.FourBitTim((253, 48)))];

    [Fact]
    public void InPagePaletteKeepsPageWordsAndPackedAddress()
    {
        SpriteEffectPalette palette = new SpriteEffectPaletteResolver(Page, [448, 256]).Resolve(48, 253);
        Assert.Equal("effect-page", palette.Source);
        Assert.Equal((496, 509), (palette.VramX, palette.VramY));
        Assert.All(palette.Words, word => Assert.Equal(0x7FFF, word));
    }

    [Fact]
    public void SourceRowPastVramBottomWrapsIntoNeverWrittenRectangle()
    {
        // Sheet 1 of the battle overlay: placement (448, 256), part CLUT (48, 253), header row 2 and
        // animated row 2 give row 513, which the nine-bit GPU row field reads as row 1.
        SpriteEffectPalette palette = new SpriteEffectPaletteResolver(Page, [448, 256]).Resolve(48, 257);
        Assert.Equal("cleared-vram", palette.Source);
        Assert.Equal((496, 1), (palette.VramX, palette.VramY));
        Assert.Equal(new ushort[16], palette.Words);
    }

    [Theory]
    [InlineData(320, 256, 48, 257)]
    [InlineData(448, 0, 48, 300)]
    [InlineData(448, 256, 80, 257)]
    [InlineData(448, 256, 48, -300)]
    public void OtherOutOfPageAddressesStayUnsupported(int x, int y, int clutX, int clutY)
    {
        var resolver = new SpriteEffectPaletteResolver(Page, [x, y]);
        Assert.Throws<FormatException>(() => resolver.Resolve(clutX, clutY));
    }

    [Fact]
    public void UnknownPlacementNeverResolvesOutsideThePage()
    {
        var resolver = new SpriteEffectPaletteResolver(Page, null);
        Assert.Equal((-1, -1), (resolver.Resolve(48, 253).VramX, resolver.Resolve(48, 253).VramY));
        Assert.Throws<FormatException>(() => resolver.Resolve(48, 257));
    }

    [Fact]
    public void MalformedPlacementIsRejected()
    {
        Assert.Throws<FormatException>(() => new SpriteEffectPaletteResolver(Page, [448]));
    }
}
