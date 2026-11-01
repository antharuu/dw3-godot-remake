using Godot;
using Dw3Production.Simulation;

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

	public InputState State { get; } = new();
	public bool F3On { get; private set; }
	public bool HasFocus { get; private set; } = true;

	public void Handle(InputEvent inputEvent)
	{
		if (!HasFocus) return;

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

	public void SetFocused(bool focused)
	{
		HasFocus = focused;
		if (!focused) Clear();
	}

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

	private void HandleKey(InputEventKey keyEvent)
	{
		if (keyEvent.Echo) return;

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
			if (keyEvent.Pressed && !keyEvent.Echo) State.PressF8();
			return;
		}

		var pressed = keyEvent.Pressed;
		if (keyEvent.Keycode is Key.Shift)
		{
			State.SetWalkHeld(pressed);
			return;
		}
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
				if (pressed) State.PressAction();
				else State.ReleaseAction();
				return;
			case Key.X:
				// Circle / cancel (the battle menu's back button), AZERTY-friendly.
				if (pressed) State.PressCancel();
				else State.ReleaseCancel();
				return;
			// Keyboard aliases for PSX L1/R1. PageUp/PageDown do not collide with exploration.
			case Key.Pageup:
				if (pressed) State.PressPreviousPage();
				return;
			case Key.Pagedown:
				if (pressed) State.PressNextPage();
				return;
			default:
				return;
		}
		ApplyDirections();
	}

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

	private void HandleJoypadButton(InputEventJoypadButton buttonEvent)
	{
		if (buttonEvent.ButtonIndex == JoyButton.A)
		{
			if (buttonEvent.Pressed) State.PressAction();
			else State.ReleaseAction();
		}
		else if (buttonEvent.ButtonIndex == JoyButton.B)
		{
			if (buttonEvent.Pressed) State.PressCancel();
			else State.ReleaseCancel();
		}
		else if (buttonEvent.ButtonIndex == JoyButton.LeftShoulder && buttonEvent.Pressed)
			State.PressPreviousPage();
		else if (buttonEvent.ButtonIndex == JoyButton.RightShoulder && buttonEvent.Pressed)
			State.PressNextPage();
	}

	private void ApplyDirections() => State.SetDirections(
		left: _keyLeft || _stickLeft,
		right: _keyRight || _stickRight,
		up: _keyUp || _stickUp,
		down: _keyDown || _stickDown);
}
