using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Input;

/// <summary>
/// Converts native Godot events to held semantic controls. Keyboard and left-stick
/// sources are kept separate so releasing one source never clears the other.
/// </summary>
public sealed class InputAdapter
{
    private const float StickDeadZone = 0.5f;
    private bool _keyLeft;
    private bool _keyRight;
    private bool _keyUp;
    private bool _keyDown;
    private bool _stickLeft;
    private bool _stickRight;
    private bool _stickUp;
    private bool _stickDown;

    /// <summary>Gets the current semantic input state.</summary>
    public InputState State { get; } = new();

    /// <summary>Gets a value indicating whether the F3 debug overlay is currently active.</summary>
    public bool F3On { get; private set; }

    /// <summary>Gets a value indicating whether the window has keyboard focus.</summary>
    public bool HasFocus { get; private set; } = true;

    /// <summary>Processes a raw Godot input event and updates the semantic state.</summary>
    /// <param name="inputEvent">The Godot input event to process.</param>
    public void Handle(InputEvent inputEvent)
    {
        if (!HasFocus)
        {
            return;
        }

        switch (inputEvent)
        {
            case InputEventKey keyEvent:
                HandleKey(keyEvent);
                break;
            case InputEventJoypadMotion motionEvent:
                HandleJoypadMotion(motionEvent);
                break;
            case InputEventJoypadButton buttonEvent:
                HandleJoypadButton(buttonEvent);
                break;
        }
    }

    /// <summary>Updates the focus state and clears held inputs when focus is lost.</summary>
    /// <param name="focused">True when the window gains focus; false when it loses it.</param>
    public void SetFocused(bool focused)
    {
        HasFocus = focused;
        if (!focused)
        {
            Clear();
        }
    }

    /// <summary>Clears all held keyboard and stick directions and resets the semantic state.</summary>
    public void Clear()
    {
        _keyLeft = false;
        _keyRight = false;
        _keyUp = false;
        _keyDown = false;
        _stickLeft = false;
        _stickRight = false;
        _stickUp = false;
        _stickDown = false;
        State.Clear();
    }

    /// <summary>Handles keyboard events. Key repeats are ignored. F3 toggles the diagnostic overlay and F8 requests the debug battle, both matched by physical key so every layout uses the same keys. Shift holds walk, everything else goes to the direction and button mapping.</summary>
    /// <param name="keyEvent">A keyboard event, press or release.</param>
    private void HandleKey(InputEventKey keyEvent)
    {
        if (keyEvent.Echo)
        {
            return;
        }

        if (keyEvent.PhysicalKeycode == Key.F3)
        {
            if (keyEvent.Pressed)
            {
                F3On = !F3On;
                State.ToggleF3();
            }

            return;
        }

        // F8 is the debug battle-test trigger, bound by physical keycode (never by letter,
        // so AZERTY keeps the same key).
        if (keyEvent.PhysicalKeycode == Key.F8)
        {
            if (keyEvent.Pressed)
            {
                State.PressF8();
            }

            return;
        }

        if (keyEvent.Keycode is Key.Shift)
        {
            State.SetWalkHeld(keyEvent.Pressed);
            return;
        }

        HandleDirectionKey(keyEvent);
    }

    /// <summary>Maps the remaining keys. Arrows and ZQSD (AZERTY) set the held keyboard directions, Enter is Cross (confirm), X is Circle (cancel), Page Up and Page Down are L1 and R1. Only direction keys recompute the combined directions.</summary>
    /// <param name="keyEvent">A non-repeated keyboard event that is not F3, F8 or Shift.</param>
    private void HandleDirectionKey(InputEventKey keyEvent)
    {
        bool pressed = keyEvent.Pressed;
        switch (keyEvent.Keycode)
        {
            case Key.Q:
            case Key.Left:
                _keyLeft = pressed;
                break;
            case Key.D:
            case Key.Right:
                _keyRight = pressed;
                break;
            case Key.Z:
            case Key.Up:
                _keyUp = pressed;
                break;
            case Key.S:
            case Key.Down:
                _keyDown = pressed;
                break;
            case Key.Enter:
                if (pressed)
                {
                    State.PressAction();
                }
                else
                {
                    State.ReleaseAction();
                }

                return;
            case Key.X:
                // Circle / cancel (the battle menu's back button), AZERTY-friendly.
                if (pressed)
                {
                    State.PressCancel();
                }
                else
                {
                    State.ReleaseCancel();
                }

                return;
            // Keyboard aliases for PSX L1/R1. PageUp/PageDown do not collide with exploration.
            case Key.Pageup:
                if (pressed)
                {
                    State.PressPreviousPage();
                }

                return;
            case Key.Pagedown:
                if (pressed)
                {
                    State.PressNextPage();
                }

                return;
            default:
                return;
        }

        ApplyDirections();
    }

    /// <summary>Turns the left stick into four digital directions. An axis counts as held past ±0.5, which matches the digital pad of the original controller and ignores stick drift.</summary>
    /// <param name="motionEvent">An axis event. Axes other than the left stick X and Y are ignored.</param>
    private void HandleJoypadMotion(InputEventJoypadMotion motionEvent)
    {
        if (motionEvent.Axis == JoyAxis.LeftX)
        {
            _stickLeft = motionEvent.AxisValue <= -StickDeadZone;
            _stickRight = motionEvent.AxisValue >= StickDeadZone;
            ApplyDirections();
            return;
        }

        if (motionEvent.Axis == JoyAxis.LeftY)
        {
            _stickUp = motionEvent.AxisValue <= -StickDeadZone;
            _stickDown = motionEvent.AxisValue >= StickDeadZone;
            ApplyDirections();
        }
    }

    /// <summary>Maps controller buttons: south (A) to Cross, east (B) to Circle, left and right shoulders to L1 and R1. Shoulders only act on press, so a page turn fires once per press.</summary>
    /// <param name="buttonEvent">A controller button event.</param>
    private void HandleJoypadButton(InputEventJoypadButton buttonEvent)
    {
        if (buttonEvent.ButtonIndex == JoyButton.A)
        {
            if (buttonEvent.Pressed)
            {
                State.PressAction();
            }
            else
            {
                State.ReleaseAction();
            }
        }
        else if (buttonEvent.ButtonIndex == JoyButton.B)
        {
            if (buttonEvent.Pressed)
            {
                State.PressCancel();
            }
            else
            {
                State.ReleaseCancel();
            }
        }
        else if (buttonEvent.ButtonIndex == JoyButton.LeftShoulder && buttonEvent.Pressed)
        {
            State.PressPreviousPage();
        }
        else if (buttonEvent.ButtonIndex == JoyButton.RightShoulder && buttonEvent.Pressed)
        {
            State.PressNextPage();
        }
    }

    /// <summary>Publishes the held directions to <see cref="State"/>, OR-ing keyboard and stick so either source can hold a direction while the other releases it.</summary>
    private void ApplyDirections() => State.SetDirections(
        left: _keyLeft || _stickLeft,
        right: _keyRight || _stickRight,
        up: _keyUp || _stickUp,
        down: _keyDown || _stickDown);
}
