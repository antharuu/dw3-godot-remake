using Dw3Production.Diagnostics.Battle;
using Godot;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleEntrancePixelsTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void LocalActorBrightnessIsNotDilutedByBlackMargins(int stride)
    {
        var pixels = new byte[1280 * 720 * stride];
        pixels[((400 * 1280) + 700) * stride] = 97;
        Assert.Equal(1, BattleEntrancePixels.CountBright(pixels, 1280, stride, new Rect2I(695, 390, 149, 277)));
        Assert.Equal(0, BattleEntrancePixels.CountBright(pixels, 1280, stride, new Rect2I(456, 249, 214, 241)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AnyRgbChannelAbove96IsRejected(int channel)
    {
        var pixels = new byte[] { 96, 96, 96, 255 };
        var region = new Rect2I(0, 0, 1, 1);
        Assert.Equal(0, BattleEntrancePixels.CountBright(pixels, 1, 4, region));
        pixels[channel] = 97;
        Assert.Equal(1, BattleEntrancePixels.CountBright(pixels, 1, 4, region));
    }

    [Fact]
    public void RegionEndIsExclusive()
    {
        byte[] pixels = [0, 0, 0, 255, 255, 255];
        Assert.Equal(0, BattleEntrancePixels.CountBright(pixels, 2, 3, new Rect2I(0, 0, 1, 1)));
        Assert.Equal(1, BattleEntrancePixels.CountBright(pixels, 2, 3, new Rect2I(1, 0, 1, 1)));
    }
}
