using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Runs pack-free native host integration assertions in an isolated headless Godot process.</summary>
public partial class NativeRootMenuProof : Node
{
    /// <inheritdoc/>
    public override void _Ready()
    {
        try
        {
            ConfirmAndCancel();
            NativeMenuLifecycleProof.FocusLifecycle();
            NativeMenuLifecycleProof.DeviceOverlap();
            NativeMenuLifecycleProof.CompetingButtons();
            NativeMenuCommandsProof.Openers();
            NativeMenuCommandsProof.Unavailable();
            NativeMenuCommandsProof.Navigation();
            NativeMenuCommandsProof.Lists();
            NativeMenuCommandsProof.TechniqueExecution();
            NativeMenuCommandsProof.DigivolveExecution();
            NativeItemUseProof.ApplyHealingAndStatusItems();
            NativePreparedItemTechniqueProof.RunWhenRequested();
            NativeMenuPhaseProof.AttackAndReentry();
            NativeMenuPhaseProof.Flee();
            NativeMenuPhaseProof.EnemyTurn();
            NativeMenuPhaseProof.NonMenuConsumers();
            NativePairSwitchProof.VoluntarySwitch();
            NativePairSwitchProof.PairAction();
            NativePairSwitchProof.PairRefusals();
            NativePairSwitchProof.ForcedReplacement();
            GD.Print("NATIVE_ROOT_MENU_PROOF PASS first=confirm-cancel ingress=GameHost._Input tick=GameHost.TickBattleMenu coordinator=production");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PrintErr($"NATIVE_ROOT_MENU_PROOF FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    /// <summary>Proves the real root callback opens a technique page and real cancellation returns to root.</summary>
    private static void ConfirmAndCancel()
    {
        using var session = new NativeMenuSession();
        session.Tap(Key.Down);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Technique && session.Menu.View.Lines.Count == 6, "FIRST: production technique opener not reached");
        session.Tap(Key.X);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Root && session.Menu.Lines.Count == 6, "FIRST: production cancellation not reached");
        ProbeRequirement.Require(session.Rng.Count == 0 && session.Flow.State.ActivePlayerFighter?.Mp == 50 && session.Flow.ItemStock[0x2B] == 2, "FIRST: navigation spent resources");
    }
}
