using Dw3Production.Diagnostics.Field;
using Dw3Production.Input;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Checks real attack/flee consumers and noninteractive phase input ownership.</summary>
internal static class NativeMenuPhaseProof
{
    /// <summary>Executes attack through the host and requires frozen identities, HP and RNG during playback.</summary>
    public static void AttackAndReentry()
    {
        using var session = new NativeMenuSession();
        session.Flow.PlayerBasicTech.Script = 5;
        session.Flow.PlayerBasicTech.Power = 1000;
        session.Flow.PlayerBasicTech.Mp = 2;
        session.KeyEvent(Key.Enter, true);
        ProbeRequirement.Require(session.Flow.Phase == BattleFlowPhase.ActionSequence && session.Flow.PendingSequenceContext is { AttackerId: 1, TargetId: 2 }, "root attack did not reach sequence consumer");
        ProbeRequirement.Require(session.Rng.Count > 0 && session.Flow.State.ActiveEnemyFighter?.Hp < 999, "attack did not resolve a real hit");
        string before = NativeMenuCommandsProof.Snapshot(session);
        for (int index = 0; index < 8; index++)
        {
            session.KeyEvent(Key.Enter, true, echo: true);
            session.Tick();
        }

        ProbeRequirement.Require(NativeMenuCommandsProof.Snapshot(session) == before, "playback rerolled or changed HP/MP");
        session.KeyEvent(Key.Enter, false);
        session.KeyEvent(Key.Enter, true);
        session.Flow.CompleteActionSequence();
        AdvanceEnemy(session);
        session.Menu.OpenRoot(session.Flow.RootMenuLines());
        before = NativeMenuCommandsProof.Snapshot(session);
        session.Tick();
        ProbeRequirement.Require(NativeMenuCommandsProof.Snapshot(session) == before, "playback press queued an action on next turn");
        session.KeyEvent(Key.Enter, true);
        ProbeRequirement.Require(NativeMenuCommandsProof.Snapshot(session) == before, "held cross repeated across turns");
        session.KeyEvent(Key.Enter, false);
        for (int turn = 0; turn < 3; turn++)
        {
            session.KeyEvent(Key.Enter, true);
            ProbeRequirement.Require(session.Flow.Phase == BattleFlowPhase.ActionSequence && NativeMenuCommandsProof.Snapshot(session) != before, "fresh cross failed after reentry");
            session.Flow.CompleteActionSequence();
            AdvanceEnemy(session);
            session.Menu.OpenRoot(session.Flow.RootMenuLines());
            before = NativeMenuCommandsProof.Snapshot(session);
            session.KeyEvent(Key.Enter, true);
            session.Tick();
            ProbeRequirement.Require(NativeMenuCommandsProof.Snapshot(session) == before, "held cross repeated during repeated turn transition");
            session.KeyEvent(Key.Enter, false);
        }

        GD.Print("NATIVE_ROOT_MENU_PROOF PASS attack=real-consumer playback=frozen-hp-identities-rng reentry=fresh-only");
    }

    /// <summary>Requires native menu events during an enemy turn to leave the real consumer unchanged.</summary>
    public static void EnemyTurn()
    {
        using var session = new NativeMenuSession(enemyFirst: true);
        ProbeRequirement.Require(session.Flow.Phase == BattleFlowPhase.EnemyTurn, "fixture enemy opening turn missing");
        string before = NativeMenuCommandsProof.Snapshot(session);
        session.Tap(Key.Enter);
        session.Tap(Key.X);
        session.Tap(Key.Down);
        session.Button(JoyButton.A, true);
        session.Button(JoyButton.A, false);
        ProbeRequirement.Require(NativeMenuCommandsProof.Snapshot(session) == before && !session.Input.ActionPressed, "enemy-turn input consumed or queued a player command");
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS enemy-turn=no-cost-no-queued-command");
    }

    /// <summary>Executes flee at root row five, then refuses input in the ended phase.</summary>
    public static void Flee()
    {
        using var session = new NativeMenuSession();
        NativeMenuCommandsProof.Select(session, 5);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Flow.Outcome == BattleOutcome.PlayerFled && session.Flow.Phase == BattleFlowPhase.Ended && session.Rng.Count > 0, "real root flee consumer not reached");
        string before = NativeMenuCommandsProof.Snapshot(session);
        session.Tap(Key.Enter);
        session.Tap(Key.X);
        session.Tap(Key.Down);
        ProbeRequirement.Require(NativeMenuCommandsProof.Snapshot(session) == before, "ended input consumed resources");
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS flee=real-consumer ended=no-cost");
    }

    /// <summary>Checks the ingress helper keeps report and exploration confirmation usable.</summary>
    public static void NonMenuConsumers()
    {
        using var session = new NativeMenuSession();
        session.Flow.EndBattle(BattleOutcome.Won);
        var adapter = new InputAdapter();
        using var press = new InputEventKey { Keycode = Key.Enter, Pressed = true };
        using var release = new InputEventKey { Keycode = Key.Enter, Pressed = false };
        BattleInputGate.Handle(adapter, press, session.Flow, hasFightReport: true);
        ProbeRequirement.Require(adapter.State.ConsumeActionPressed(), "report confirmation was filtered by menu gate");
        BattleInputGate.Handle(adapter, release, null, hasFightReport: false);
        BattleInputGate.Handle(adapter, press, null, hasFightReport: false);
        ProbeRequirement.Require(adapter.State.ConsumeActionPressed(), "exploration confirmation was filtered by menu gate");
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS ingress-helper=report-exploration-confirm-preserved");
    }

    /// <summary>Completes only the existing scheduler-owned enemy action for fixture progression.</summary>
    /// <param name="session">Native host fixture.</param>
    private static void AdvanceEnemy(NativeMenuSession session)
    {
        if (session.Flow.Phase == BattleFlowPhase.EnemyTurn)
        {
            session.Flow.EnemyAct(session.Rng);
        }

        ProbeRequirement.Require(session.Flow.Phase == BattleFlowPhase.PlayerCommand, "fixture did not return to player turn");
    }
}
