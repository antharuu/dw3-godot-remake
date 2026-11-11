using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleSequenceCameraTests
{
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
