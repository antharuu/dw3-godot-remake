using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

public sealed class HudFrameRendererPaletteTests
{
    private static readonly HudTexturePage[] Pages = [Page()];

    [Fact]
    public void ResolvedZeroPaletteLeavesEveryTexelTransparent()
    {
        HudRgbaImage image = HudFrameRenderer.RenderPartWithPalette(Pages, new HudSpritePart(0, 0, 2, 1, 0, 0, 0), new ushort[16], true);
        Assert.Equal((2, 1), (image.Width, image.Height));
        Assert.Equal(new byte[8], image.Pixels);
    }

    [Fact]
    public void ResolvedPaletteColoursTexelsWithStpAlpha()
    {
        var palette = new ushort[16];
        palette[1] = 0x801F;
        HudRgbaImage image = HudFrameRenderer.RenderPartWithPalette(Pages, new HudSpritePart(0, 0, 1, 1, 0, 0, 0), palette, true);
        Assert.Equal([255, 0, 0, 128], image.Pixels);
    }

    [Fact]
    public void InvalidPartOrPaletteWidthIsRejected()
    {
        Assert.Throws<FormatException>(() => HudFrameRenderer.RenderPartWithPalette(Pages, new HudSpritePart(0, 0, 1, 1, 0, 0, 1), new ushort[16], false));
        Assert.Throws<FormatException>(() => HudFrameRenderer.RenderPartWithPalette(Pages, new HudSpritePart(0, 0, 0, 1, 0, 0, 0), new ushort[16], false));
        Assert.Throws<FormatException>(() => HudFrameRenderer.RenderPartWithPalette(Pages, new HudSpritePart(0, 0, 1, 1, 0, 0, 0), new ushort[15], false));
        Assert.Throws<ArgumentNullException>(() => HudFrameRenderer.RenderPartWithPalette(Pages, null!, new ushort[16], false));
    }

    private static HudTexturePage Page()
    {
        var rows = new ushort[HudTexturePage.PageHeight][];
        for (int y = 0; y < rows.Length; y++)
        {
            rows[y] = new ushort[HudTexturePage.PageWidthWords];
        }

        rows[0][0] = 0x0011;
        return new HudTexturePage(rows);
    }
}
