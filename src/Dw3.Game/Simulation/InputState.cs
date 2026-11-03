namespace Dw3Production.Simulation;

/// <summary>
/// One of the eight input directions, numbered clockwise on screen from up.
/// The facing values stored on the disc use the same order rotated by half a turn
/// (disc facing 0 is down), so a facing is <c>(direction + 4) % 8</c>.
/// </summary>
public enum SemanticDirection
{
    /// <summary>No direction held, or an impossible combination such as up and down together.</summary>
    None = -1,

    /// <summary>Toward the top of the screen.</summary>
    Up = 0,

    /// <summary>Diagonal between up and right.</summary>
    UpRight = 1,

    /// <summary>Toward the right edge of the screen.</summary>
    Right = 2,

    /// <summary>Diagonal between down and right.</summary>
    DownRight = 3,

    /// <summary>Toward the bottom of the screen.</summary>
    Down = 4,

    /// <summary>Diagonal between down and left.</summary>
    DownLeft = 5,

    /// <summary>Toward the left edge of the screen.</summary>
    Left = 6,

    /// <summary>Diagonal between up and left.</summary>
    UpLeft = 7,
}

/// <summary>
/// Engine-independent semantic input sampled by the fixed simulation clock.
/// One-shot flags are cleared only by their corresponding consume method.
/// </summary>
public sealed class InputState
{
    /// <summary>Gets a value indicating whether a left direction is held on the keyboard or the stick.</summary>
    public bool HeldLeft { get; private set; }

    /// <summary>Gets a value indicating whether a right direction is held on the keyboard or the stick.</summary>
    public bool HeldRight { get; private set; }

    /// <summary>Gets a value indicating whether an up direction is held on the keyboard or the stick.</summary>
    public bool HeldUp { get; private set; }

    /// <summary>Gets a value indicating whether a down direction is held on the keyboard or the stick.</summary>
    public bool HeldDown { get; private set; }

    /// <summary>Gets a value indicating whether the walk modifier is held. The original player always runs while a direction is held, so this is an added modifier, not a run button.</summary>
    public bool WalkHeld { get; private set; }

    /// <summary>Gets a value indicating whether Cross was pressed since the last <see cref="ConsumeActionPressed"/>. Set once per press, never by a held button.</summary>
    public bool ActionPressed { get; private set; }

    /// <summary>Gets a value indicating whether Cross is currently down. Used to ignore a press until the button is released, so one press never opens and advances a dialogue at once.</summary>
    public bool ActionHeld { get; private set; }

    /// <summary>Gets a value indicating whether F3 was pressed since the last <see cref="ConsumeF3Toggled"/>.</summary>
    public bool F3Toggled { get; private set; }

    /// <summary>Gets a value indicating whether Circle was pressed since the last <see cref="ConsumeCancelPressed"/>. The battle menu reads it to leave a submenu.</summary>
    public bool CancelPressed { get; private set; }

    /// <summary>Gets a value indicating whether Circle is currently down, so a held button produces a single cancel.</summary>
    public bool CancelHeld { get; private set; }

    /// <summary>Gets a value indicating whether L1 was pressed since the last consume. The Technique and Item menus use it to show the previous page.</summary>
    public bool PreviousPagePressed { get; private set; }

    /// <summary>Gets a value indicating whether R1 was pressed since the last consume. The Technique and Item menus use it to show the next page.</summary>
    public bool NextPagePressed { get; private set; }

    /// <summary>Gets a value indicating whether the debug battle key F8 was pressed since the last <see cref="ConsumeF8Pressed"/>.</summary>
    public bool F8Pressed { get; private set; }

    /// <summary>
    /// Maps four directional booleans to a <see cref="SemanticDirection"/>.
    /// Shared by <see cref="Direction"/> and by <c>GameHost.DirectionToward</c> to avoid
    /// duplicating the eight-way tuple switch.
    /// </summary>
    /// <param name="up">Up is held.</param>
    /// <param name="down">Down is held.</param>
    /// <param name="left">Left is held.</param>
    /// <param name="right">Right is held.</param>
    /// <returns>One of the eight directions, or <see cref="SemanticDirection.None"/> when nothing is held or opposite directions are held together.</returns>
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

    /// <summary>Gets the direction formed by the four held flags, <see cref="SemanticDirection.None"/> when they cancel out.</summary>
    public SemanticDirection Direction => DirectionFrom(HeldUp, HeldDown, HeldLeft, HeldRight);

    /// <summary>Replaces the four held direction flags at once, so the direction never passes through a half-updated state during a tick.</summary>
    /// <param name="left">Left is held.</param>
    /// <param name="right">Right is held.</param>
    /// <param name="up">Up is held.</param>
    /// <param name="down">Down is held.</param>
    public void SetDirections(bool left, bool right, bool up, bool down)
    {
        HeldLeft = left;
        HeldRight = right;
        HeldUp = up;
        HeldDown = down;
    }

    /// <summary>Sets whether the walk modifier is held.</summary>
    /// <param name="held"><see langword="true"/> while the modifier key is down.</param>
    public void SetWalkHeld(bool held) => WalkHeld = held;

    /// <summary>Records Cross going down. The one-shot flag is raised only when the button was up, so key repeats and a held button do not produce new presses.</summary>
    public void PressAction()
    {
        if (!ActionHeld)
        {
            ActionPressed = true;
        }

        ActionHeld = true;
    }

    /// <summary>Records Cross going up, which arms the next press.</summary>
    public void ReleaseAction() => ActionHeld = false;

    /// <summary>Records Circle going down. The one-shot flag is raised only when the button was up.</summary>
    public void PressCancel()
    {
        if (!CancelHeld)
        {
            CancelPressed = true;
        }

        CancelHeld = true;
    }

    /// <summary>Records Circle going up, which arms the next cancel.</summary>
    public void ReleaseCancel() => CancelHeld = false;

    /// <summary>Raises the L1 one-shot flag. Callers only call it on press, so no held state is tracked.</summary>
    public void PressPreviousPage() => PreviousPagePressed = true;

    /// <summary>Raises the R1 one-shot flag. Callers only call it on press, so no held state is tracked.</summary>
    public void PressNextPage() => NextPagePressed = true;

    /// <summary>Raises the debug battle one-shot flag.</summary>
    public void PressF8() => F8Pressed = true;

    /// <summary>Raises the F3 one-shot flag. The overlay state itself is kept by the input adapter, this only tells the tick that it changed.</summary>
    public void ToggleF3() => F3Toggled = true;

    /// <summary>Reads and clears the Cross one-shot flag, so exactly one consumer acts on a press.</summary>
    /// <returns><see langword="true"/> once per press, then <see langword="false"/> until the next one.</returns>
    public bool ConsumeActionPressed()
    {
        var pressed = ActionPressed;
        ActionPressed = false;
        return pressed;
    }

    /// <summary>Reads and clears the Circle one-shot flag.</summary>
    /// <returns><see langword="true"/> once per press, then <see langword="false"/> until the next one.</returns>
    public bool ConsumeCancelPressed()
    {
        var pressed = CancelPressed;
        CancelPressed = false;
        return pressed;
    }

    /// <summary>Reads and clears the L1 one-shot flag.</summary>
    /// <returns><see langword="true"/> once per press, then <see langword="false"/> until the next one.</returns>
    public bool ConsumePreviousPagePressed()
    {
        var pressed = PreviousPagePressed;
        PreviousPagePressed = false;
        return pressed;
    }

    /// <summary>Reads and clears the R1 one-shot flag.</summary>
    /// <returns><see langword="true"/> once per press, then <see langword="false"/> until the next one.</returns>
    public bool ConsumeNextPagePressed()
    {
        var pressed = NextPagePressed;
        NextPagePressed = false;
        return pressed;
    }

    /// <summary>Reads and clears the debug battle one-shot flag.</summary>
    /// <returns><see langword="true"/> once per F8 press.</returns>
    public bool ConsumeF8Pressed()
    {
        var pressed = F8Pressed;
        F8Pressed = false;
        return pressed;
    }

    /// <summary>Reads and clears the F3 one-shot flag.</summary>
    /// <returns><see langword="true"/> once per F3 press.</returns>
    public bool ConsumeF3Toggled()
    {
        var toggled = F3Toggled;
        F3Toggled = false;
        return toggled;
    }

    /// <summary>Releases every held flag and drops pending one-shot flags. Called when the window loses focus so no key stays stuck down.</summary>
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
