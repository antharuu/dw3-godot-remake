using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

/// <summary>
/// Covers the two part orders of a multi-part sprite frame: the historical back-to-front composition keeps the first
/// record on top, while the SpriteDrawer draw order keeps the last record on top.
/// </summary>
public sealed class HudAlphaCompositorOrderTests
{
    [Fact]
    public void DrawOrderPutsTheLastRecordOnTop()
    {
        HudRenderedPart[] parts = [Solid(0, 0, 10), Solid(0, 0, 20)];

        (HudRgbaImage image, int minX, int minY) = HudAlphaCompositor.ComposeInDrawOrder(parts);

        Assert.Equal((0, 0), (minX, minY));
        Assert.Equal(20, image.Pixels[0]);
    }

    [Fact]
    public void BackToFrontKeepsTheFirstRecordOnTop()
    {
        HudRenderedPart[] parts = [Solid(0, 0, 10), Solid(0, 0, 20)];

        (HudRgbaImage image, _, _) = HudAlphaCompositor.ComposeBackToFront(parts);

        Assert.Equal(10, image.Pixels[0]);
    }

    [Fact]
    public void DrawOrderKeepsTheUnionBoundsAndOffsets()
    {
        HudRenderedPart[] parts = [Solid(2, 3, 10), Solid(4, 1, 20)];

        (HudRgbaImage image, int minX, int minY) = HudAlphaCompositor.ComposeInDrawOrder(parts);

        Assert.Equal((2, 1), (minX, minY));
        Assert.Equal((3, 3), (image.Width, image.Height));
    }

    private static HudRenderedPart Solid(int dx, int dy, byte red) =>
        new(dx, dy, new HudRgbaImage([red, 0, 0, 255], 1, 1));
}
