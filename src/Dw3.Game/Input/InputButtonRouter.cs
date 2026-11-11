using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Input;

/// <summary>Routes native Cross and Circle sources without letting a partial release re-arm the aggregate.</summary>
internal sealed class InputButtonRouter
{
    private readonly InputButtonLatch _action = new();
    private readonly InputButtonLatch _cancel = new();

    /// <summary>Disarms held controls across a native focus boundary.</summary>
    public void Suspend()
    {
        _action.Suspend();
        _cancel.Suspend();
    }

    /// <summary>Handles mapped native buttons, including releases and holds received without focus.</summary>
    /// <param name="input">Native event.</param>
    /// <param name="state">Semantic destination.</param>
    /// <param name="focused">Current focus gate.</param>
    /// <returns>True when this event belongs to Cross or Circle.</returns>
    public bool Handle(InputEvent input, InputState state, bool focused)
    {
        return input switch
        {
            InputEventKey { Keycode: Key.Enter, Echo: false } key => Apply(_action, -1, key.Pressed, focused, state.PressAction, state.ReleaseAction),
            InputEventKey { Keycode: Key.X, Echo: false } key => Apply(_cancel, -1, key.Pressed, focused, state.PressCancel, state.ReleaseCancel),
            InputEventJoypadButton { ButtonIndex: JoyButton.A } pad => Apply(_action, pad.Device, pad.Pressed, focused, state.PressAction, state.ReleaseAction),
            InputEventJoypadButton { ButtonIndex: JoyButton.B } pad => Apply(_cancel, pad.Device, pad.Pressed, focused, state.PressCancel, state.ReleaseCancel),
            _ => false,
        };
    }

    /// <summary>Publishes a single aggregate transition to the semantic state.</summary>
    /// <param name="latch">Button source tracker.</param>
    /// <param name="source">Physical source id.</param>
    /// <param name="pressed">Physical state.</param>
    /// <param name="focused">Focus gate.</param>
    /// <param name="press">Semantic press.</param>
    /// <param name="release">Semantic release.</param>
    /// <returns>True because the button was recognized.</returns>
    private static bool Apply(InputButtonLatch latch, int source, bool pressed, bool focused, Action press, Action release)
    {
        if (latch.Update(source, pressed, focused))
        {
            press();
        }
        else if (!latch.Held)
        {
            release();
        }

        return true;
    }
}
