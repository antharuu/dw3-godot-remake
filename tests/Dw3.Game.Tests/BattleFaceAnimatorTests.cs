using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleFaceAnimatorTests
{
    [Fact]
    public void SynchronizeFrom_CopiesTheSourceClockBeforeApplyingTheSourceMotion()
    {
        var source = new BattleFaceAnimator([]);
        var target = new BattleFaceAnimator([]);

        source.SetBlinkTimer(6);
        source.Update(0, 4);
        target.SynchronizeFrom(source, 4);

        Assert.Equal(source.State, target.State);
    }
}
