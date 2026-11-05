using System.Text.Json;
using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleFaceAnimatorTests
{
    [Fact]
    public void SynchronizeFrom_CopiesCommittedBlinkFrameWithoutConsumingPartnerRandom()
    {
        var source = CreateConfiguredAnimator();
        var target = CreateConfiguredAnimator();

        source.SetBlinkTimer(2);
        source.Update(1, 1);
        target.SetRandom(() => throw new InvalidOperationException("partner face must not draw random"));
        target.SynchronizeFrom(source, 1);

        Assert.Equal(source.State, target.State);
        Assert.Equal(source.FrameState, target.FrameState);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(6, 1, 1)]
    [InlineData(10, 2, 1)]
    [InlineData(0, 1, 4)]
    public void SynchronizeFrom_CopiesConfiguredEyeAndNonEyeFrames(int blinkTimer, int elapsed, int motion)
    {
        var source = CreateConfiguredAnimator();
        var target = CreateConfiguredAnimator();

        source.SetRandom(() => 0);
        source.SetBlinkTimer(blinkTimer);
        source.Update(elapsed, motion);
        target.SynchronizeFrom(source, motion);

        Assert.Equal(source.State, target.State);
        Assert.Equal(source.FrameState, target.FrameState);
    }

    private static BattleFaceAnimator CreateConfiguredAnimator()
    {
        var animator = new BattleFaceAnimator([]);
        using var document = JsonDocument.Parse("""
            {"face":{"parts":[
              {"index":0,"x":8,"y":8,"w":1,"h":1,"frames":[[0,0],[1,0],[2,0]]},
              {"index":2,"x":8,"y":9,"w":1,"h":1,"frames":[[0,1],[1,1],[2,1]]}
            ]}}
            """);
        animator.Configure(document.RootElement);
        return animator;
    }
}
