using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Records real menu-command boundaries and unpinned stage motion through the production battle loop.</summary>
internal sealed class BattleActionProbe : BattleProbe
{
    private int _simulationTicks;

    /// <summary>Initializes a new instance of the <see cref="BattleActionProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleActionProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_ACTION_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory("probe/battle-action");
        var encounter = System.Environment.GetEnvironmentVariable("DW3_BATTLE_ACTION_ENCOUNTER");
        if (encounter is null)
        {
            Host.StartDebugBattle();
        }
        else
        {
            Host.StartEncounterForProbe(int.Parse(encounter, System.Globalization.CultureInfo.InvariantCulture));
        }

        ProbeRequirement.Require(Host.HasActiveBattle, "F8 did not start a battle");
        ProbeRequirement.Require(Host.Flow.Phase == BattleFlowPhase.PlayerCommand, "opening phase is not PlayerCommand");
        await CaptureAttackBoundariesAsync(captureRoot, "kotemon", 383);
        await SwitchToRenamonAsync();
        await CaptureAttackBoundariesAsync(captureRoot, "renamon", 373);
        await BattleStageActionProof.RunAsync(Host, captureRoot);
        QuietLog.Info($"battle-action PASS ticks={_simulationTicks} stage={Host.Renderer.CaptureStageMotionForProbe()?.Frame} captures={captureRoot}");
    }

    /// <summary>Formats a source-coordinate rectangle without claiming it is a full model silhouette.</summary>
    /// <param name="bounds">Difference bounds measured in the captured framebuffer.</param>
    /// <returns>A compact integer rectangle.</returns>
    private static string FormatBounds(Rect2 bounds) => $"({bounds.Position.X:0},{bounds.Position.Y:0},{bounds.Size.X:0},{bounds.Size.Y:0})";

    /// <summary>Logs localized framebuffer differences between the three generic command-boundary captures.</summary>
    /// <param name="label">Stable active-fighter label.</param>
    /// <param name="before">Before-Cross framebuffer.</param>
    /// <param name="consumed">Post-consumption framebuffer.</param>
    /// <param name="returned">Post-return framebuffer.</param>
    private static void LogFrameDifferences(string label, Image before, Image consumed, Image returned)
    {
        var consumedBounds = BattlePixelMetrics.DifferenceBoundsAboveThreshold(before, consumed, threshold: 30);
        var returnedBounds = BattlePixelMetrics.DifferenceBoundsAboveThreshold(before, returned, threshold: 30);
        if (consumedBounds is not { } consumedRect || returnedBounds is not { } returnedRect)
        {
            throw new InvalidOperationException($"{label} action boundaries made no framebuffer change");
        }

        QuietLog.Info($"battle-action difference fighter={label} before_to_consumed={FormatBounds(consumedRect)} before_to_return={FormatBounds(returnedRect)} qualification=generic-command-boundaries-not-psx-action-trajectory");
    }

    /// <summary>Captures before-Cross, command-consumed, and command-return boundaries for a real active fighter.</summary>
    /// <param name="captureRoot">Directory receiving the three boundary captures.</param>
    /// <param name="label">Stable fighter-size label used in file and log names.</param>
    /// <param name="expectedActor">Expected active player fighter identifier.</param>
    /// <returns>A task completed after the command returns to the root menu.</returns>
    private async Task CaptureAttackBoundariesAsync(string captureRoot, string label, int expectedActor)
    {
        RequireCommandRoot(expectedActor);
        while (Host.Menu.Cursor > 0)
        {
            TickInput(Key.Z, true);
            TickInput(Key.Z, false);
        }

        var scheduledTurn = Host.Flow.State.NextPlayerTurn;
        if (System.Environment.GetEnvironmentVariable("DW3_BATTLE_ACTION_MENU_ONLY") == "1")
        {
            SelectTechniqueCommand();
        }

        var before = await CaptureBoundaryAsync(captureRoot, label, "before-cross", expectedActor);
        TickInput(Key.Enter, true);
        var consumed = await CaptureBoundaryAsync(captureRoot, label, "command-consumed", expectedActor);
        SetKey(Key.Enter, false);
        if (System.Environment.GetEnvironmentVariable("DW3_BATTLE_ACTION_MENU_ONLY") == "1")
        {
            TickInput(Key.X, true);
            TickInput(Key.X, false);
        }

        ProbeRequirement.Require(BattleActionEvidence.IsConsumed(scheduledTurn, Host.Flow.State.NextPlayerTurn), "NOT an action: native input did not spend a player turn");
        AdvanceToPlayerCommand(expectedActor);
        RequireCommandRoot(expectedActor);
        var returned = await CaptureBoundaryAsync(captureRoot, label, "command-return", expectedActor);
        LogFrameDifferences(label, before, consumed, returned);
    }

    /// <summary>Runs the real switch command path until Renamon owns the next player command turn.</summary>
    /// <returns>A task completed once the active fighter is Renamon at the command root.</returns>
    private Task SwitchToRenamonAsync()
    {
        RequireCommandRoot(383);
        Tap(Key.S, 3);
        Tap(Key.Enter, 4);
        ProbeRequirement.Require(Host.Flow.State.ActivePlayerFighter?.Id == 373, "live switch path did not select Renamon");
        Tap(Key.X, 3);
        AdvanceToPlayerCommand(373);
        RequireCommandRoot(373);
        return Task.CompletedTask;
    }

    /// <summary>Captures a real framebuffer and writes the complete live boundary state to the diagnostic log.</summary>
    /// <param name="captureRoot">Directory receiving the PNG.</param>
    /// <param name="fighterLabel">Stable player-fighter label.</param>
    /// <param name="boundary">Command boundary label.</param>
    /// <param name="expectedActor">Expected active player fighter identifier.</param>
    /// <returns>The captured framebuffer.</returns>
    private async Task<Image> CaptureBoundaryAsync(string captureRoot, string fighterLabel, string boundary, int expectedActor)
    {
        var image = await BattleProbeCapture.CaptureAsync(Host, captureRoot, $"{fighterLabel}-{boundary}.png");
        var actor = Host.Flow.State.ActivePlayerFighter?.Id ?? 0;
        var target = Host.Flow.State.ActiveEnemyFighter?.Id ?? 0;
        ProbeRequirement.Require(actor == expectedActor, $"boundary actor={actor} target={target} expected_actor={expectedActor}");
        var file = Path.Combine(captureRoot, $"{fighterLabel}-{boundary}.png");
        QuietLog.Info($"battle-action boundary={boundary} actor={actor} target={target} phase={Host.Flow.Phase} menu={Host.Menu.Menu} tick={_simulationTicks} time_ms={Time.GetTicksMsec()} player_hp={Host.Flow.State.ActivePlayerFighter?.Hp} target_hp={Host.Flow.State.ActiveEnemyFighter?.Hp} next_player_turn={Host.Flow.State.NextPlayerTurn} next_enemy_turn={Host.Flow.State.NextEnemyTurn} file={file}");
        return image;
    }

    /// <summary>Advances the actual simulation without adding an input edge until the specified actor owns a root command turn.</summary>
    /// <param name="expectedActor">Expected active player fighter identifier.</param>
    private void AdvanceToPlayerCommand(int expectedActor)
    {
        const int limit = 128;
        for (var guard = 0; guard < limit; guard++)
        {
            if (Host.Flow.Phase == BattleFlowPhase.PlayerCommand && Host.Menu.Menu == BattleMenuKind.Root && Host.Flow.State.ActivePlayerFighter?.Id == expectedActor)
            {
                return;
            }

            TickSimulation();
        }

        throw new InvalidOperationException($"command did not return actor={expectedActor} phase={Host.Flow.Phase} menu={Host.Menu.Menu}");
    }

    /// <summary>Requires that the current state is an ordinary root command turn for the selected active fighter.</summary>
    /// <param name="expectedActor">Expected active player fighter identifier.</param>
    private void RequireCommandRoot(int expectedActor) =>
        ProbeRequirement.Require(
            Host.Flow.Phase == BattleFlowPhase.PlayerCommand && Host.Menu.Menu == BattleMenuKind.Root && Host.Flow.State.ActivePlayerFighter?.Id == expectedActor,
            $"root command unavailable phase={Host.Flow.Phase} menu={Host.Menu.Menu} actor={Host.Flow.State.ActivePlayerFighter?.Id} expected={expectedActor}");

    /// <summary>Selects the real root Technique command, whose Cross boundary opens and then returns from a production submenu.</summary>
    private void SelectTechniqueCommand()
    {
        TickInput(Key.S, true);
        TickInput(Key.S, false);
        ProbeRequirement.Require(Host.Menu.Cursor == 1, $"Technique cursor={Host.Menu.Cursor} expected=1");
    }

    /// <summary>Drives complete native presses while counting every simulation tick.</summary>
    /// <param name="key">Key to press.</param>
    /// <param name="times">Number of complete presses.</param>
    private void Tap(Key key, int times)
    {
        for (var press = 0; press < times; press++)
        {
            TickInput(key, true);
            TickInput(key, false);
        }
    }

    /// <summary>Sends a native edge without advancing simulation, preserving accepted-command state.</summary>
    /// <param name="key">Native key.</param>
    /// <param name="pressed">Whether this edge is pressed.</param>
    private void SetKey(Key key, bool pressed) =>
        Host.Input.Handle(new InputEventKey { Keycode = key, Pressed = pressed });

    /// <summary>Sends one native input edge through the live input adapter and advances one fixed simulation tick.</summary>
    /// <param name="key">Key edge sent to the production menu.</param>
    /// <param name="pressed">Whether the edge presses or releases the key.</param>
    private void TickInput(Key key, bool pressed)
    {
        SetKey(key, pressed);
        TickSimulation();
    }

    /// <summary>Advances exactly one normal fixed battle simulation tick.</summary>
    private void TickSimulation()
    {
        Host.TickTestSimulation();
        _simulationTicks++;
    }
}
