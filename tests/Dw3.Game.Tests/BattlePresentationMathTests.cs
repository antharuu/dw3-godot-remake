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
    public void SourceClockCarriesSixtyHzTicksAcrossPresentationCadences()
    {
        var clock = new BattleSourceClock();
        Assert.Equal(1, clock.Advance(1f / 60f));
        Assert.Equal(2, clock.Advance(1f / 30f));
        Assert.Equal(0, clock.Advance(1f / 120f));
        Assert.Equal(1, clock.Advance(1f / 120f));
    }

    [Fact]
    public void SourceClockReplaysTheSameShotStateAtThirtySixtyAndOneTwentyHz()
    {
        var expected = ReplayShotAtOneSecond(60);

        Assert.Equal(expected, ReplayShotAtOneSecond(30));
        Assert.Equal(expected, ReplayShotAtOneSecond(120));
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

    [Theory]
    [InlineData(0f)]
    [InlineData(.25f)]
    [InlineData(.5f)]
    [InlineData(.75f)]
    [InlineData(1f)]
    public void EntranceFadeConsumesOneDirectionalAttenuationAfterSdkNormalization(float fraction)
    {
        var direction = new Vector3(4, -2, 1).Normalized();
        var source = new BattleModel.BattleLightSet(
            [new BattleModel.BattleLight(direction, new Color(.8f, .6f, .4f))],
            new Color(.2f, .1f, .05f));
        var fade = new BattleEntranceFade();
        fade.SetFraction(fraction);

        AssertConsumedShade(source, fade, direction);
        AssertConsumedShade(source, fade, new Vector3(3, -1, 2).Normalized());
    }

    [Fact]
    public void EntranceFadeConsumesAmbientWithoutDividingAZeroDirection()
    {
        var fade = new BattleEntranceFade();
        fade.SetFraction(.5f);
        var source = new BattleModel.BattleLightSet(
            [new BattleModel.BattleLight(Vector3.Zero, new Color(.8f, .6f, .4f))],
            new Color(.2f, .1f, .05f));

        var actual = Shade(fade.Scale(source), Vector3.Right);

        AssertColor(new Color(.1f, .05f, .025f), actual);
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

    private static (int List, int Shot, int Time, int Substate, int Rotation) ReplayShotAtOneSecond(int cadence)
    {
        var camera = new BattleShotCamera(new FixedShotRng(), enemyCameraCount: 3);
        var clock = new BattleSourceClock();
        for (var frame = 0; frame < cadence; frame++)
        {
            camera.StepFrames(clock.Advance(1f / cadence));
        }

        return (camera.List, camera.Shot, camera.Time, camera.Substate, camera.View.RotationY);
    }

    private static void AssertConsumedShade(BattleModel.BattleLightSet source, BattleEntranceFade fade, Vector3 normal)
    {
        var directional = Mathf.Max(0f, source.Lights[0].Direction.Normalized().Dot(normal));
        var expected = (source.Ambient * fade.Fraction) + (source.Lights[0].Rgb * (fade.Fraction * directional));
        AssertColor(expected, Shade(fade.Scale(source), normal));
    }

    private static void AssertColor(Color expected, Color actual)
    {
        Assert.Equal(expected.R, actual.R, 5);
        Assert.Equal(expected.G, actual.G, 5);
        Assert.Equal(expected.B, actual.B, 5);
    }

    private static Color Shade(BattleModel.BattleLightSet set, Vector3 normal)
    {
        var method = typeof(BattleModelLighting).GetMethod("Shade", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (Color)method.Invoke(null, [set, normal])!;
    }

    private sealed class FixedShotRng : IShotCameraRng
    {
        /// <inheritdoc/>
        public int NextRoll() => 0;
    }
}
