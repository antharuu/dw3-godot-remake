using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleSequenceCameraTests
{
    [Fact]
    public void FadeInterpolatesAuthoredRotationBeforeWorldComposition()
    {
        var camera = new BattleCameraFade();
        var views = new BattleShotCameraViews { GetEnemyView = () => new(), GetFighterView = (_, _) => new() };
        short[] start = [0, 1, 0, 0, 256, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 300];
        short[] end = [2, 1, 0, 0, 256, 0, 0, 0, 0, 0, 0, 0, 1024, 0, 0, 300];
        BattleSequenceCamera.Apply(new CameraRequestEvent(BattleSide.Player, start), views, camera);
        BattleSequenceCamera.Apply(new CameraRequestEvent(BattleSide.Player, end), views, camera);
        camera.Step(1f / 60);
        var expected = BattleSceneMath.ComposeCameraPosition(new SourceBattleCameraView { Vpz = 256, RotationY = 512, Projection = 300 }, false);
        Assert.Equal(expected.X, camera.Position.X, 4);
        Assert.Equal(expected.Z, camera.Position.Z, 4);
    }

    [Fact]
    public void FixedPointProgressKeepsItsAuthoredTruncationAcrossCadences()
    {
        var transition = new BattleSequenceCameraTransition();
        transition.Seed(new SourceBattleCameraView { Projection = 280 });
        transition.Begin(new SourceBattleCameraView { Vpx = 4096, Projection = 280 }, 20);
        transition.Step(20f / 60);
        Assert.Equal(4080, transition.Current.Vpx);
        transition.Step(1f / 60);
        Assert.Equal(4096, transition.Current.Vpx);
        var split = new BattleSequenceCameraTransition();
        split.Seed(new SourceBattleCameraView { Projection = 280 });
        split.Begin(new SourceBattleCameraView { Vpx = 4096, Projection = 280 }, 20);
        for (int i = 0; i < 42; i++)
        {
            split.Step(1f / 120);
        }

        Assert.Equal(transition.Current.Vpx, split.Current.Vpx);
    }

    [Fact]
    public void ExplicitCutAndFadeChangePhysicalCameraState()
    {
        var camera = new BattleCameraFade();
        var views = new BattleShotCameraViews { GetEnemyView = () => new(), GetFighterView = (_, _) => new() };
        short[] words = [0, 1, 256, 512, 768, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 300];
        var cut = new CameraRequestEvent(BattleSide.Player, words);
        BattleSequenceCamera.Apply(cut, views, camera);
        Assert.Equal(300, camera.Projection);
        Assert.Equal(1, camera.Position.X);
        words = [2, 1, 768, 512, 768, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 400];
        BattleSequenceCamera.Apply(new CameraRequestEvent(BattleSide.Player, words), views, camera);
        Assert.Equal(1, camera.Position.X);
        camera.Step(1f / 60);
        Assert.Equal(2, camera.Position.X);
        Assert.Equal(350, camera.Projection);
        camera.Step(1f / 60);
        Assert.Equal(3, camera.Position.X);
        Assert.Equal(400, camera.Projection);
    }
}
