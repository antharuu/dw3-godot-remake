using Dw3Production.Input;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class InputButtonLatchTests
{
    [Fact]
    public void PartialReleaseDoesNotRearmAnotherHeldSource()
    {
        var button = new InputButtonLatch();
        Assert.True(button.Update(-1, true, true));
        Assert.False(button.Update(19, true, true));
        Assert.False(button.Update(-1, false, true));
        Assert.True(button.Held);
        Assert.False(button.Update(19, true, true));
        Assert.False(button.Update(19, false, true));
        Assert.False(button.Held);
        Assert.True(button.Update(19, true, true));
    }

    [Fact]
    public void FocusLossRequiresEverySourceToRelease()
    {
        var button = new InputButtonLatch();
        Assert.True(button.Update(-1, true, true));
        button.Suspend();
        Assert.False(button.Update(19, true, false));
        Assert.False(button.Update(-1, true, true));
        Assert.False(button.Update(-1, false, true));
        Assert.False(button.Update(19, true, true));
        Assert.False(button.Update(19, false, true));
        Assert.True(button.Update(-1, true, true));
    }

    [Fact]
    public void PressWhileUnfocusedCannotBecomeAnEdgeOnReturn()
    {
        var button = new InputButtonLatch();
        button.Suspend();
        Assert.False(button.Update(19, true, false));
        Assert.False(button.Update(19, true, true));
        Assert.False(button.Update(19, false, true));
        Assert.True(button.Update(19, true, true));
    }

    [Fact]
    public void IndependentControllersRemainSeparateSources()
    {
        var button = new InputButtonLatch();
        Assert.True(button.Update(3, true, true));
        Assert.False(button.Update(19, true, true));
        Assert.False(button.Update(3, false, true));
        Assert.False(button.Update(19, true, true));
        Assert.False(button.Update(19, false, true));
        Assert.True(button.Update(3, true, true));
    }
}
