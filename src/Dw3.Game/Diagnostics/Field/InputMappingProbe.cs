using Dw3Production.Input;
using Dw3Production.Logging;
using Dw3Production.Simulation;
using Godot;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Checks the input mapping on a private <see cref="InputAdapter"/>: ZQSD and arrow keys, diagonals,
/// key echo, the one-shot Cross edge from Enter and the pad, the physical F3 toggle, the left stick
/// and the release of every held input on focus loss. It needs no field scene.
/// </summary>
internal static class InputMappingProbe
{
    /// <summary>Runs every mapping check and logs the final adapter state.</summary>
    /// <exception cref="InvalidOperationException">A key, button or focus event was mapped wrongly.</exception>
    public static void Run()
    {
        var probe = new InputAdapter();
        CheckKeyboardDirections(probe);
        CheckActionsAndF3(probe);
        CheckStickAndFocus(probe);
        QuietLog.Info($"input probe PASS direction={probe.State.Direction} f3_on={probe.F3On} focused={probe.HasFocus}");
    }

    /// <summary>Checks ZQSD, echo, the Z+D diagonal and the four arrow keys.</summary>
    /// <param name="probe">Adapter under test.</param>
    private static void CheckKeyboardDirections(InputAdapter probe)
    {
        Press(probe, Key.Z);
        Require(probe.State.HeldUp && probe.State.Direction == SemanticDirection.Up, "Z did not hold up");
        probe.Handle(new InputEventKey { Keycode = Key.Z, Pressed = true, Echo = true });
        Require(probe.State.HeldUp, "echo changed held state");
        Press(probe, Key.D);
        Require(probe.State.Direction == SemanticDirection.UpRight, "Z+D diagonal mapping failed");
        Release(probe, Key.Z);
        Require(probe.State.Direction == SemanticDirection.Right, "key release failed");
        Release(probe, Key.D);
        CheckSingleKey(probe, Key.Q, SemanticDirection.Left, "Q did not map left");
        CheckSingleKey(probe, Key.S, SemanticDirection.Down, "S did not map down");
        CheckSingleKey(probe, Key.Left, SemanticDirection.Left, "left arrow did not map left");
        CheckSingleKey(probe, Key.Up, SemanticDirection.Up, "up arrow did not map up");
        CheckSingleKey(probe, Key.Right, SemanticDirection.Right, "right arrow did not map right");
        CheckSingleKey(probe, Key.Down, SemanticDirection.Down, "down arrow did not map down");
    }

    /// <summary>Checks the one-shot Enter and pad A action edges and the physical F3 toggle.</summary>
    /// <param name="probe">Adapter under test.</param>
    private static void CheckActionsAndF3(InputAdapter probe)
    {
        Press(probe, Key.Enter);
        Require(probe.State.ConsumeActionPressed() && !probe.State.ConsumeActionPressed(), "Enter action edge was not one-shot");
        Release(probe, Key.Enter);
        probe.Handle(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true });
        Require(probe.State.ConsumeActionPressed(), "joypad action did not map");
        probe.Handle(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = false });
        probe.Handle(new InputEventKey { PhysicalKeycode = Key.F3, Pressed = true });
        Require(probe.F3On && probe.State.ConsumeF3Toggled(), "physical F3 did not toggle on");
        probe.Handle(new InputEventKey { PhysicalKeycode = Key.F3, Pressed = false });
        probe.Handle(new InputEventKey { PhysicalKeycode = Key.F3, Pressed = true });
        Require(!probe.F3On && probe.State.ConsumeF3Toggled(), "physical F3 did not toggle off");
    }

    /// <summary>Checks the left stick, then that focus loss clears and blocks input until focus returns.</summary>
    /// <param name="probe">Adapter under test.</param>
    private static void CheckStickAndFocus(InputAdapter probe)
    {
        probe.Handle(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -1.0f });
        Require(probe.State.HeldLeft, "left stick did not hold left");
        probe.SetFocused(false);
        Require(!probe.State.HeldLeft && probe.State.Direction == SemanticDirection.None, "focus loss did not clear state");
        Press(probe, Key.Z);
        Require(!probe.State.HeldUp, "unfocused input was admitted");
        probe.SetFocused(true);
        Require(!probe.State.HeldLeft, "focus return manufactured input");
    }

    /// <summary>Presses one key, checks the resulting direction and releases it.</summary>
    /// <param name="probe">Adapter under test.</param>
    /// <param name="key">Logical key code.</param>
    /// <param name="expected">Direction the key must hold.</param>
    /// <param name="failure">Failure message.</param>
    private static void CheckSingleKey(InputAdapter probe, Key key, SemanticDirection expected, string failure)
    {
        Press(probe, key);
        Require(probe.State.Direction == expected, failure);
        Release(probe, key);
    }

    /// <summary>Sends a logical key press.</summary>
    /// <param name="probe">Adapter under test.</param>
    /// <param name="key">Logical key code.</param>
    private static void Press(InputAdapter probe, Key key) => probe.Handle(new InputEventKey { Keycode = key, Pressed = true });

    /// <summary>Sends a logical key release.</summary>
    /// <param name="probe">Adapter under test.</param>
    /// <param name="key">Logical key code.</param>
    private static void Release(InputAdapter probe, Key key) => probe.Handle(new InputEventKey { Keycode = key, Pressed = false });
}
