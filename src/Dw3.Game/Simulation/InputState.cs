namespace Dw3Production.Simulation;

public enum SemanticDirection
{
    None = -1,
    Up = 0,
    UpRight = 1,
    Right = 2,
    DownRight = 3,
    Down = 4,
    DownLeft = 5,
    Left = 6,
    UpLeft = 7,
}

/// <summary>
/// Engine-independent semantic input sampled by the fixed simulation clock.
/// One-shot flags are cleared only by their corresponding consume method.
/// </summary>
public sealed class InputState
{
    public bool HeldLeft { get; private set; }
    public bool HeldRight { get; private set; }
    public bool HeldUp { get; private set; }
    public bool HeldDown { get; private set; }
    // The original player runs while a direction is held. This is a deliberate
    // temporary walk modifier, not a fabricated run button.
    public bool WalkHeld { get; private set; }
    public bool ActionPressed { get; private set; }
    public bool ActionHeld { get; private set; }
    public bool F3Toggled { get; private set; }
    // Circle (cancel), consumed by the battle menu's submenus. One-shot, like ActionPressed.
    public bool CancelPressed { get; private set; }
    public bool CancelHeld { get; private set; }
    // L1/R1, consumed by the source-paginated Technique and Item menus.
    public bool PreviousPagePressed { get; private set; }
    public bool NextPagePressed { get; private set; }
    // F8, the debug battle-test trigger. One-shot on press, consumed by GameHost.
    public bool F8Pressed { get; private set; }

    /// <summary>
    /// Maps four directional booleans to a <see cref="SemanticDirection"/>.
    /// Shared by <see cref="Direction"/> and by <c>GameHost.DirectionToward</c> to avoid
    /// duplicating the eight-way tuple switch.
    /// </summary>
    public static SemanticDirection DirectionFrom(bool up, bool down, bool left, bool right) =>
        (up, down, left, right) switch
        {
            (true, false, false, false) => SemanticDirection.Up,
            (true, false, false, true) => SemanticDirection.UpRight,
            (false, false, false, true) => SemanticDirection.Right,
            (false, true, false, true) => SemanticDirection.DownRight,
            (false, true, false, false) => SemanticDirection.Down,
            (false, true, true, false) => SemanticDirection.DownLeft,
            (false, false, true, false) => SemanticDirection.Left,
            (true, false, true, false) => SemanticDirection.UpLeft,
            _ => SemanticDirection.None,
        };

    public SemanticDirection Direction => DirectionFrom(HeldUp, HeldDown, HeldLeft, HeldRight);

    public void SetDirections(bool left, bool right, bool up, bool down)
    {
        HeldLeft = left;
        HeldRight = right;
        HeldUp = up;
        HeldDown = down;
    }

    public void SetWalkHeld(bool held) => WalkHeld = held;

    public void PressAction()
    {
        if (!ActionHeld)
        {
            ActionPressed = true;
        }

        ActionHeld = true;
    }

    public void ReleaseAction() => ActionHeld = false;

    public void PressCancel()
    {
        if (!CancelHeld)
        {
            CancelPressed = true;
        }

        CancelHeld = true;
    }

    public void ReleaseCancel() => CancelHeld = false;

    public void PressPreviousPage() => PreviousPagePressed = true;

    public void PressNextPage() => NextPagePressed = true;

    public void PressF8() => F8Pressed = true;

    public void ToggleF3() => F3Toggled = true;

    public bool ConsumeActionPressed()
    {
        var pressed = ActionPressed;
        ActionPressed = false;
        return pressed;
    }

    public bool ConsumeCancelPressed()
    {
        var pressed = CancelPressed;
        CancelPressed = false;
        return pressed;
    }

    public bool ConsumePreviousPagePressed()
    {
        var pressed = PreviousPagePressed;
        PreviousPagePressed = false;
        return pressed;
    }

    public bool ConsumeNextPagePressed()
    {
        var pressed = NextPagePressed;
        NextPagePressed = false;
        return pressed;
    }

    public bool ConsumeF8Pressed()
    {
        var pressed = F8Pressed;
        F8Pressed = false;
        return pressed;
    }

    public bool ConsumeF3Toggled()
    {
        var toggled = F3Toggled;
        F3Toggled = false;
        return toggled;
    }

    public void Clear()
    {
        SetDirections(left: false, right: false, up: false, down: false);
        WalkHeld = false;
        ActionPressed = false;
        ActionHeld = false;
        CancelPressed = false;
        CancelHeld = false;
        PreviousPagePressed = false;
        NextPagePressed = false;
        F8Pressed = false;
        F3Toggled = false;
    }
}
