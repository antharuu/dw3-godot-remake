using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class LegacyInputStateConsoleTests
{
    [Fact]
    public void PreservesLegacyConsoleAssertions()
    {
        HeldReleaseAndDirection();
        DiagonalAndStop();
        ClearResetsHeldAndEdges();
        CancelAndF8AreOneShot();
    }

    private static void HeldReleaseAndDirection()
    {
        var state = new InputState();
        state.SetDirections(left: true, right: false, up: false, down: false);
        Check(state.HeldLeft && state.Direction == SemanticDirection.Left, "left is held");
        state.SetDirections(left: false, right: true, up: false, down: false);
        Check(!state.HeldLeft && state.HeldRight && state.Direction == SemanticDirection.Right, "direction changes cleanly");
    }

    private static void DiagonalAndStop()
    {
        var state = new InputState();
        state.SetDirections(left: false, right: true, up: true, down: false);
        Check(state.Direction == SemanticDirection.UpRight, "up-right diagonal");
        state.SetDirections(left: false, right: false, up: false, down: false);
        Check(!state.HeldLeft && !state.HeldRight && !state.HeldUp && !state.HeldDown, "all released");
        Check(state.Direction == SemanticDirection.None, "stop has no direction");
    }

    private static void ClearResetsHeldAndEdges()
    {
        var state = new InputState();
        state.SetDirections(left: true, right: false, up: true, down: false);
        state.PressAction();
        state.ToggleF3();
        state.Clear();
        Check(state.Direction == SemanticDirection.None, "clear directions");
        Check(!state.ActionPressed && !state.ActionHeld && !state.F3Toggled, "clear one-shot edges and held action");
        state.PressAction();
        Check(state.ActionHeld && state.ConsumeActionPressed() && !state.ConsumeActionPressed(), "action consumes once while held");
        state.ReleaseAction();
        state.PressAction();
        Check(state.ConsumeActionPressed(), "action re-arms after release");
        state.ToggleF3();
        Check(state.ConsumeF3Toggled() && !state.ConsumeF3Toggled(), "F3 consumes once");
    }

    private static void CancelAndF8AreOneShot()
    {
        var state = new InputState();
        state.PressCancel();
        Check(state.CancelHeld && state.ConsumeCancelPressed() && !state.ConsumeCancelPressed(), "cancel consumes once while held");
        state.ReleaseCancel();
        state.PressCancel();
        Check(state.ConsumeCancelPressed(), "cancel re-arms after release");
        state.PressF8();
        Check(state.ConsumeF8Pressed() && !state.ConsumeF8Pressed(), "F8 consumes once");
        state.Clear();
        Check(!state.CancelHeld && !state.CancelPressed && !state.F8Pressed, "clear resets cancel and F8");
    }

    private static void Check(bool condition, string name)
    {
        Assert.True(condition, name);
    }
}
