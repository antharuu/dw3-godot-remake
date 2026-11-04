using Dw3Production.Presentation;
using Godot;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Pins the fixed-point and clock arithmetic of the battle presentation helpers with fabricated
/// values: key-track sampling, the PAL clock, the stage entrance fade and the camera fade.
/// </summary>
public sealed class BattlePresentationMathTests
{
    private static readonly List<Vector4> TwoKeys = [new(0, 0, 0, 0), new(4096, -100, 50, 10)];

    [Fact]
    public void SampleInterpolatesWithA12BitFactorAndArithmeticShift()
    {
        Assert.Equal(new Vector3(2048, -50, 25), BattleKeyTrack.Sample(TwoKeys, 5));
        Assert.Equal(new Vector3(409, -10, 4), BattleKeyTrack.Sample(TwoKeys, 1));
    }

    [Fact]
    public void SampleHoldsTheEndKeysAndReturnsZeroForAnEmptyChannel()
    {
        Assert.Equal(Vector3.Zero, BattleKeyTrack.Sample(TwoKeys, -3));
        Assert.Equal(new Vector3(4096, -100, 50), BattleKeyTrack.Sample(TwoKeys, 10));
        Assert.Equal(new Vector3(4096, -100, 50), BattleKeyTrack.Sample(TwoKeys, 99));
        Assert.Equal(Vector3.Zero, BattleKeyTrack.Sample([], 4));
    }

    [Fact]
    public void FirstKeyAtOrAfterTakesTheNextKeyOrTheLastOne()
    {
        Assert.Equal(new Vector3(4096, -100, 50), BattleKeyTrack.FirstKeyAtOrAfter(TwoKeys, 3));
        Assert.Equal(Vector3.Zero, BattleKeyTrack.FirstKeyAtOrAfter(TwoKeys, 0));
        Assert.Equal(new Vector3(4096, -100, 50), BattleKeyTrack.FirstKeyAtOrAfter(TwoKeys, 11));
    }

    [Fact]
    public void FixedBlendAndHasZeroFollowTheIntegerRules()
    {
        Assert.Equal(new Vector3(50, -50, 4), BattleKeyTrack.FixedBlend(Vector3.Zero, new Vector3(100, -100, 8), 2048));
        Assert.True(BattleKeyTrack.HasZero(new Vector3(1, 0, 1)));
        Assert.False(BattleKeyTrack.HasZero(new Vector3(1, -1, 1)));
    }

    [Fact]
    public void PalClockCarriesTheFractionBetweenCalls()
    {
        var clock = new BattlePalClock();
        Assert.Equal(12, clock.Advance(0.25f));
        Assert.Equal(13, clock.Advance(0.25f));
        clock.Reset();
        Assert.Equal(0, clock.Advance(0.01f));
        Assert.Equal(1, clock.Advance(0.01f));
    }

    [Fact]
    public void EntranceFadeAdvancesByWholeFramesAndStopsAtFull()
    {
        var fade = new BattleEntranceFade();
        Assert.True(fade.Advance(0.5f));
        Assert.Equal(25 * (0x1000 / 60), fade.Level);
        Assert.True(fade.Advance(10f));
        Assert.Equal(BattleEntranceFade.FullLevel, fade.Level);
        Assert.False(fade.Advance(1f));
        Assert.Equal(1f, fade.Fraction);
        fade.Restart();
        Assert.Equal(0, fade.Level);
    }

    [Fact]
    public void EntranceFadeWritesBlackAsOneAndRampsTheStageGrey()
    {
        var fade = new BattleEntranceFade();
        Assert.Equal(new Color(1f / 255f, 1f / 255f, 1f / 255f), fade.Background(new Color(1f, 1f, 1f)));
        Assert.Equal(new Color(0, 0, 0), fade.StageColor());
        fade.SetFraction(2f);
        Assert.Equal(new Color(0x80 / 255f, 0x80 / 255f, 0x80 / 255f), fade.StageColor());
    }

    [Fact]
    public void EntranceFadeInterpolatesLightDirectionsAlongsideTheirColours()
    {
        var fade = new BattleEntranceFade();
        var source = new BattleModel.BattleLightSet(
            [new BattleModel.BattleLight(new Vector3(4, -2, 1), new Color(.8f, .6f, .4f))],
            new Color(.2f, .1f, .05f));
        fade.SetFraction(.5f);

        var scaled = fade.Scale(source);

        Assert.Equal(new Vector3(2, -1, .5f), scaled.Lights[0].Direction);
        Assert.Equal(new Color(.4f, .3f, .2f, .5f), scaled.Lights[0].Rgb);
        Assert.Equal(new Color(.1f, .05f, .025f, .5f), scaled.Ambient);
    }

    [Fact]
    public void CameraFadeSnapsOrReachesItsTargetOnTheLastStep()
    {
        var fade = new BattleCameraFade();
        var target = new Vector3(1, 2, 3);
        fade.Begin(target, Vector3.Zero, 100f, 1f, snap: false);
        Assert.Equal(new Vector3(0, 6.5f, 22f), fade.Position);
        fade.Step(1f / 60f);
        Assert.Equal(target, fade.Position);
        Assert.Equal(100f, fade.Projection);

        fade.Begin(new Vector3(4, 4, 4), Vector3.One, 200f, 0.1f, snap: true);
        Assert.Equal(new Vector3(4, 4, 4), fade.Position);
        Assert.Equal(Vector3.One, fade.Target);
        Assert.Equal(200f, fade.Projection);
    }
}
