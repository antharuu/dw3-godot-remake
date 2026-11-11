using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Exercises real native device overlap and host focus notifications, without OS input.</summary>
internal static class NativeMenuLifecycleProof
{
    /// <summary>Requires a second source release not to re-arm a still held confirmation.</summary>
    public static void DeviceOverlap()
    {
        using var session = new NativeMenuSession();
        session.Tap(Key.Down);
        string before = NativeMenuCommandsProof.Snapshot(session);
        session.KeyEvent(Key.Enter, true);
        session.Button(JoyButton.A, true);
        session.KeyEvent(Key.Enter, false);
        session.Button(JoyButton.A, true);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Technique && NativeMenuCommandsProof.Snapshot(session) == before, "device overlap re-armed held Cross and spent technique");
        session.Button(JoyButton.A, false);
        session.Button(JoyButton.B, true);
        session.Button(JoyButton.B, false);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Root, "controller cancel did not return to root");
        session.Stick(1);
        session.KeyEvent(Key.Down, true);
        session.Stick(0);
        ProbeRequirement.Require(session.Input.HeldDown && session.Menu.Cursor == 1, "stick release cleared keyboard direction or repeated it");
        session.KeyEvent(Key.Down, false);
        session.Stick(1);
        ProbeRequirement.Require(session.Menu.Cursor == 2, "new controller direction failed after keyboard release");
        session.Stick(0);
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS devices=keyboard-pad-overlap-release-alternation");
    }

    /// <summary>Requires cancellation to consume a competing confirmation in the same sample.</summary>
    public static void CompetingButtons()
    {
        using var session = new NativeMenuSession();
        session.Tap(Key.Down);
        session.Tap(Key.Enter);
        string before = NativeMenuCommandsProof.Snapshot(session);
        session.KeyEvent(Key.Enter, true, tick: false);
        session.KeyEvent(Key.X, true);
        session.Tick();
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Root && NativeMenuCommandsProof.Snapshot(session) == before, "cancel leaked competing Cross to root on next tick");
        session.KeyEvent(Key.Enter, false);
        session.KeyEvent(Key.X, false);
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS competing-buttons=cancel-wins-no-buffered-confirm");
    }

    /// <summary>Requires focus return and held native events not to replay confirmation.</summary>
    public static void FocusLifecycle()
    {
        using var session = new NativeMenuSession();
        session.Tap(Key.Down);
        string before = NativeMenuCommandsProof.Snapshot(session);
        session.KeyEvent(Key.Enter, true);
        session.Focus(false);
        session.KeyEvent(Key.Down, true);
        session.Button(JoyButton.A, true);
        session.KeyEvent(Key.Enter, true);
        ProbeRequirement.Require(!session.Input.ActionPressed && !session.Input.HeldDown, "unfocused input escaped adapter");
        session.Focus(true);
        session.KeyEvent(Key.Enter, true, echo: true);
        session.Button(JoyButton.A, true);
        session.KeyEvent(Key.Enter, true);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Technique && NativeMenuCommandsProof.Snapshot(session) == before, "focus return replayed held Cross");
        session.KeyEvent(Key.Enter, false);
        session.Button(JoyButton.A, false);
        session.KeyEvent(Key.Down, false);
        session.Tap(Key.X);
        session.Tap(Key.Down);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Technique, "fresh post-focus press did not re-arm");
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS focus=loss-absent-events-return-held-fresh");
    }
}
