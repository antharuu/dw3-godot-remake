using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Exercises the complete debug battle route and its field-return failure diagnostic.</summary>
internal sealed class BattlePlayProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattlePlayProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattlePlayProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_PLAY_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        ProbeRequirement.Require(Host.HasEncounterData, "encounter data unavailable");
        var captureRoot = Host.PrepareCaptureDirectory("captures");
        var beforePosition = await PrepareFieldAsync(captureRoot);
        var kotemon = await StartBattleAsync(captureRoot);
        await ExerciseFighterTransitionsAsync(captureRoot, kotemon);
        await WinBattleAsync(captureRoot);
        await ReturnToFieldAsync(captureRoot, beforePosition);
        VerifyBattleLoadFailure();
        QuietLog.Info($"battle-play probe PASS encounter={Host.LastBattleEncounter} captures={captureRoot}/battle-play-*.png");
    }

    /// <summary>Positions the player at S200's probe spawn and captures the pre-battle field.</summary>
    /// <param name="captureRoot">Directory receiving the capture.</param>
    /// <returns>The field position that the battle must restore.</returns>
    private async Task<NumericsVector2> PrepareFieldAsync(string captureRoot)
    {
        Host.ApplyS200();
        Host.State.Position = new NumericsVector2(1000, 236);
        Host.State.SetFacing(1);
        Host.State.AnimationSet = 1;
        Host.State.AnimationDirection = 1;
        await Host.CaptureStillAsync(captureRoot, "battle-play-before.png");
        var beforePosition = Host.State.Position;
        QuietLog.Info($"battle-play before zone=S200 position=({beforePosition.X:0},{beforePosition.Y:0})");
        return beforePosition;
    }

    /// <summary>Starts the manifest battle and validates its initial sourced presentation.</summary>
    /// <param name="captureRoot">Directory receiving battle captures.</param>
    /// <returns>The Kotemon frame used as the first model-transition reference.</returns>
    private async Task<Image> StartBattleAsync(string captureRoot)
    {
        Host.StartDebugBattle();
        ProbeRequirement.Require(Host.HasActiveBattle && Host.State.BattleActive, "F8 did not start a battle");
        Host.Renderer.SetProbeAnimationFrame(0);
        ProbeRequirement.Require(
            Host.LastBattleEnemyIds.Any(id => id != 0),
            $"F8 battle has no real enemy enemies=[{string.Join(",", Host.LastBattleEnemyIds)}]");
        var kotemon = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "battle-play-kotemon.png");
        await BattleProbeCapture.CaptureAsync(Host, captureRoot, "battle-text.png");
        ProbeRequirement.Require(
            Host.Menu.View.Lines.All(line => line.Text is not null && line.Text != BattleTextLine.Missing),
            "root menu has unsourced text");
        var flow = Host.Flow;
        var playerTechniques = flow.PlayerTechniques;
        var technique = playerTechniques.Count > 0 ? playerTechniques[0].Text?.Debug : null;
        QuietLog.Info($"battle-text menu=[{MenuTextDebug()}] technique={technique} message={flow.MessageText.Debug} report=FGTRPT expLabel capture={Path.Combine(captureRoot, "battle-text.png")}");
        var hudLayout = BattleRenderer.AssertHudLayout();
        await BattleProbeCapture.CaptureAsync(Host, captureRoot, "battle-hud-scale.png");
        QuietLog.Info($"battle-hud-scale PASS {hudLayout} capture={Path.Combine(captureRoot, "battle-hud-scale.png")}");
        ProbeRequirement.Require(Host.RenderedPlayerId == 383, $"F8 rendered fighter {Host.RenderedPlayerId}, expected Kotemon 383");
        Host.TickTestSimulation();
        Host.Renderer.SetCommandMenuCamera();
        var geometry = Host.Renderer.AssertOpeningGeometry(focusedFighterId: Host.RenderedPlayerId);
        QuietLog.Info($"battle-play geometry PASS {geometry}");
        QuietLog.Info($"battle-play combat player={Host.RenderedPlayerId} encounter={Host.LastBattleEncounter} enemies=[{string.Join(",", Host.LastBattleEnemyIds)}] menu={Host.Menu.View.Menu} cursor={Host.Menu.View.Cursor}");
        return kotemon;
    }

    /// <summary>Builds the log projection of the sourced root-menu lines.</summary>
    /// <returns>Comma-delimited source debug text for every root-menu line.</returns>
    private string MenuTextDebug()
    {
        var lines = new List<string>(Host.Menu.View.Lines.Count);
        foreach (var line in Host.Menu.View.Lines)
        {
            lines.Add(line.Text!.Debug);
        }

        return string.Join(",", lines);
    }

    /// <summary>Switches to Renamon, digivolves it, and proves both model transitions changed pixels.</summary>
    /// <param name="captureRoot">Directory receiving battle captures.</param>
    /// <param name="kotemon">Kotemon frame captured before the switch.</param>
    /// <returns>A task that completes after both fighter-transition captures pass.</returns>
    private async Task ExerciseFighterTransitionsAsync(string captureRoot, Image kotemon)
    {
        Host.Flow.Choose(BattleCommand.Switch, 1, Host.BattleRng);
        Host.TickTestSimulation();
        await new BattleEntranceSwitchProof(Host, captureRoot).RunAsync(outgoing: 383, incoming: 373);
        ProbeRequirement.Require(
            Host.Flow.State.ActivePlayerFighter?.Id == 373 && Host.RenderedPlayerId == 373,
            $"switch did not replace Kotemon model active={Host.Flow.State.ActivePlayerFighter?.Id} rendered={Host.RenderedPlayerId}");
        var renamon = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "battle-play-renamon.png");
        var switchPixels = BattlePixelMetrics.CountRgbChanges(kotemon, renamon, new Rect2(0, 0, kotemon.GetWidth() / 2, kotemon.GetHeight()));
        ProbeRequirement.Require(switchPixels > 500, $"switch model pixels did not change enough changed={switchPixels}");
        await AdvanceToPlayerCommandAsync();
        Host.RefreshActivePartnerCatalogs();
        var forms = Host.Flow.PlayerDigivolutions;
        var form = forms.Count > 0 ? forms[0] : 0;
        ProbeRequirement.Require(form != 0, "Renamon has no source-derived level-25 digivolution form");
        Host.Flow.Choose(BattleCommand.Digivolve, form, Host.BattleRng);
        Host.TickTestSimulation();
        ProbeRequirement.Require(
            Host.Flow.State.ActivePlayerFighter?.Id == form && Host.RenderedPlayerId == form,
            $"digivolution did not replace Renamon model active={Host.Flow.State.ActivePlayerFighter?.Id} rendered={Host.RenderedPlayerId} form={form}");
        var evolution = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "battle-play-digivolution.png");
        var evolutionPixels = BattlePixelMetrics.CountRgbChanges(renamon, evolution, new Rect2(0, 0, renamon.GetWidth() / 2, renamon.GetHeight()));
        ProbeRequirement.Require(evolutionPixels > 500, $"digivolution model pixels did not change enough changed={evolutionPixels}");
        QuietLog.Info($"battle-play model-transition kotemon=383 renamon=373 digivolution={form} switch_pixels={switchPixels} evolution_pixels={evolutionPixels}");
    }

    /// <summary>Plays the enemy turn that follows the switch through the production tick until the next command.</summary>
    /// <returns>A task that completes when the player commands again.</returns>
    private async Task AdvanceToPlayerCommandAsync()
    {
        int frames = 0;
        for (; frames < 1200 && Host.Flow.Phase != BattleFlowPhase.PlayerCommand; frames++)
        {
            Host.TickTestSimulation();
            Host.Renderer.Present(Host.BuildBattleView());
            await Host.NextFrameDrawn();
        }

        ProbeRequirement.Require(Host.Flow.Phase == BattleFlowPhase.PlayerCommand, $"enemy turn after the switch did not return to command frames={frames} phase={Host.Flow.Phase}");
    }

    /// <summary>Confirms the root attack command and waits for the terminal victory presentation.</summary>
    /// <param name="captureRoot">Directory receiving the victory capture.</param>
    /// <returns>A task that completes after the terminal victory capture.</returns>
    private async Task WinBattleAsync(string captureRoot)
    {
        SetKey(Key.S, true);
        Host.TickTestSimulation();
        SetKey(Key.S, false);
        Host.TickTestSimulation();
        ProbeRequirement.Require(Host.Menu.Cursor == 1, $"menu cursor did not move down cursor={Host.Menu.Cursor}");
        SetKey(Key.Z, true);
        Host.TickTestSimulation();
        SetKey(Key.Z, false);
        Host.TickTestSimulation();
        ProbeRequirement.Require(Host.Menu.Cursor == 0, $"menu cursor did not move up cursor={Host.Menu.Cursor}");
        SetKey(Key.Enter, true);
        Host.TickTestSimulation();
        SetKey(Key.Enter, false);
        QuietLog.Info($"battle-text action={Host.Flow.MessageText.Debug}");
        var guard = 0;
        while (Host.HasActiveBattle && Host.Flow.Phase != BattleFlowPhase.Ended && guard++ < 128)
        {
            Host.TickTestSimulation();
        }

        ProbeRequirement.Require(
            Host.HasActiveBattle && Host.Flow.Phase == BattleFlowPhase.Ended,
            $"battle did not reach a terminal outcome phase={Host.Flow.Phase} outcome={Host.Flow.Outcome}");
        QuietLog.Info($"battle-text report=FGTRPT expGained moneyGained partners={Host.BattleReport?.Partners.Count ?? 0}");
        await BattleProbeCapture.CaptureAsync(Host, captureRoot, "battle-play-victory.png");
        QuietLog.Info($"battle-play outcome={Host.Flow.Outcome} player_hp={Host.Flow.State.Players[0]!.Hp} enemy_hp={Host.Flow.State.Enemies[0]!.Hp}");
    }

    /// <summary>Advances the report input path until battle cleanup restores the original field position.</summary>
    /// <param name="captureRoot">Directory receiving the field capture.</param>
    /// <param name="beforePosition">Expected field position after report closure.</param>
    /// <returns>A task that completes after the restored-field capture.</returns>
    private async Task ReturnToFieldAsync(string captureRoot, NumericsVector2 beforePosition)
    {
        var guard = 0;
        while (Host.HasActiveBattle && guard++ < 128)
        {
            if (Host.FightReportController is null)
            {
                Host.TickTestSimulation();
            }
            else
            {
                SetKey(Key.Enter, true);
                Host.TickTestSimulation();
                SetKey(Key.Enter, false);
                Host.TickTestSimulation();
            }
        }

        ProbeRequirement.Require(!Host.HasActiveBattle && !Host.State.BattleActive, "battle did not return to the field");
        ProbeRequirement.Require(
            Host.State.Position == beforePosition,
            $"field position changed after the battle before={beforePosition} after={Host.State.Position}");
        await Host.CaptureStillAsync(captureRoot, "battle-play-after.png");
        QuietLog.Info($"battle-play after zone={Host.State.ZoneId} position=({Host.State.Position.X:0},{Host.State.Position.Y:0})");
    }

    /// <summary>Exercises the manifest-missing error path without leaving the host mutated.</summary>
    private void VerifyBattleLoadFailure()
    {
        Host.WithFighterDataUnavailable(() =>
        {
            Host.StartDebugBattle();
            ProbeRequirement.Require(
                !Host.HasActiveBattle && !Host.State.BattleActive,
                "a broken manifest still started a battle");
            ProbeRequirement.Require(
                Host.BattleNoticeText is not null && Host.IsBattleNoticeVisible,
                "the battle-load diagnostic did not set an on-screen notice");
            QuietLog.Info($"battle-play diagnostic PASS notice={Host.BattleNoticeText}");
        });
    }

    /// <summary>Injects one native battle key state without advancing simulation.</summary>
    /// <param name="key">Godot key code to inject.</param>
    /// <param name="pressed">Whether the event is a press or release.</param>
    private void SetKey(Key key, bool pressed) => Host.Input.Handle(new InputEventKey { Keycode = key, Pressed = pressed });
}
