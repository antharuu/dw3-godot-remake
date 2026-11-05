using System.Globalization;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Checks black endpoints and monotonic entrance lighting at one fixed production pose.</summary>
internal sealed class BattleEntranceProbe : BattleProbe
{
    private const int FixedPoseFrame = 10;

    /// <summary>Initializes a new instance of the <see cref="BattleEntranceProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleEntranceProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_ENTRANCE_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory("probe/battle-entrance");
        Host.ApplyS200();
        Host.StartDebugBattle();
        var renderer = Host.Renderer;
        renderer.SetProbePose(FixedPoseFrame);
        var playerId = Host.Flow.State.ActivePlayerFighter!.Id;
        var enemyId = Host.Flow.State.ActiveEnemyFighter!.Id;
        double? previousFighter = null;
        double? previousStage = null;
        foreach (var fade in new[] { 0f, .25f, .5f, .75f, 1f })
        {
            var measurements = await CaptureFadeAsync(captureRoot, fade, playerId, enemyId);
            if (fade < float.Epsilon)
            {
                ProbeRequirement.Require(measurements.Mean < 3, $"entrance frame 0 is not black mean={measurements.Mean:0.0}");
                ProbeRequirement.Require(!measurements.HudVisible, "HUD arrived before the stage fade");
                continue;
            }

            VerifyFadeProgress(fade, measurements, previousFighter, previousStage);
            VerifyHudArrival(fade, measurements.HudVisible);
            previousFighter = measurements.Fighter;
            previousStage = measurements.Stage;
        }

        await VerifyRepeatedEntranceAsync(captureRoot);
        QuietLog.Info("battle-entrance PASS");
    }

    /// <summary>
    /// Verifies observable monotonic progression without imposing an unsupported fighter-to-stage ratio.
    /// </summary>
    /// <param name="fade">Current source fade fraction.</param>
    /// <param name="current">Current fighter, stage and full-frame luminance.</param>
    /// <param name="previousFighter">Fighter luminance from the preceding non-black capture.</param>
    /// <param name="previousStage">Stage luminance from the preceding non-black capture.</param>
    private static void VerifyFadeProgress(
        float fade,
        (double Fighter, double Stage, double Mean, bool HudVisible) current,
        double? previousFighter,
        double? previousStage)
    {
        if (previousFighter is not null && previousStage is not null)
        {
            ProbeRequirement.Require(current.Fighter >= previousFighter, $"fighter darkened while entering fade={fade:0.00}");
            ProbeRequirement.Require(current.Stage >= previousStage, $"stage darkened while entering fade={fade:0.00}");
        }
    }

    /// <summary>Checks that the retained HUD appears only after the stage has reached its source endpoint.</summary>
    /// <param name="fade">Current source fade fraction.</param>
    /// <param name="hudVisible">Whether the composed canvas exposes the HUD.</param>
    private static void VerifyHudArrival(float fade, bool hudVisible)
    {
        if (fade < 1f)
        {
            ProbeRequirement.Require(!hudVisible, $"HUD arrived during stage fade={fade:0.00}");
            return;
        }

        ProbeRequirement.Require(hudVisible, "HUD did not arrive after the stage fade");
    }

    /// <summary>Starts the same encounter again and rejects a retained HUD before its own fade advances.</summary>
    /// <param name="captureRoot">Directory receiving the second-entry captures.</param>
    /// <returns>A task completed after the repeated entrance has been inspected.</returns>
    private async Task VerifyRepeatedEntranceAsync(string captureRoot)
    {
        Host.StartDebugBattle();
        var renderer = Host.Renderer;
        renderer.SetProbePose(FixedPoseFrame);
        renderer.SetLightFadeForProbe(0f);
        renderer.SetHudVisibleForProbe(true);
        var composed = await BattleProbeCapture.CaptureRawAsync(Host, captureRoot, "entrance-repeat-0.00.png");
        var mean = BattlePixelMetrics.MeanLuminance(composed);
        ProbeRequirement.Require(!renderer.EffectiveHudVisibleForProbe, "repeat entrance retained HUD");
        ProbeRequirement.Require(mean < 6, $"repeat entrance retained scene brightness mean={mean:0.0}");
        QuietLog.Info($"battle-entrance repeat fade=0.00 hud_visible={renderer.EffectiveHudVisibleForProbe} mean={mean:0.0}");
    }

    /// <summary>Captures the full and stage-only frames for one explicit fade value.</summary>
    /// <param name="captureRoot">Directory receiving both frames.</param>
    /// <param name="fade">Light interpolation in 0..1.</param>
    /// <param name="playerId">Player fighter model id.</param>
    /// <param name="enemyId">Enemy fighter model id.</param>
    /// <returns>Fighter, stage, full-frame luminance and composed HUD visibility.</returns>
    private async Task<(double Fighter, double Stage, double Mean, bool HudVisible)> CaptureFadeAsync(
        string captureRoot,
        float fade,
        int playerId,
        int enemyId)
    {
        var renderer = Host.Renderer;
        renderer.SetLightFadeForProbe(fade);
        var suffix = fade.ToString("0.00", CultureInfo.InvariantCulture);
        QuietLog.Info($"battle-entrance frame effective_fraction={renderer.LightFade:0.00} pose=idle:{FixedPoseFrame} time=fixed");
        renderer.SetHudVisibleForProbe(true);
        await BattleProbeCapture.CaptureRawAsync(Host, captureRoot, $"entrance-{suffix}-composed.png");
        renderer.SetHudVisibleForProbe(false);
        renderer.SetModelVisibleForProbe(playerId, false);
        renderer.SetModelVisibleForProbe(playerId, true);
        var full = BattleComparisonImage.ToSourceSize(
            await BattleProbeCapture.CaptureRawAsync(Host, captureRoot, $"entrance-{suffix}.png"));
        renderer.SetModelVisibleForProbe(playerId, false);
        renderer.SetModelVisibleForProbe(enemyId, false);
        var stage = BattleComparisonImage.ToSourceSize(
            await BattleProbeCapture.CaptureRawAsync(Host, captureRoot, $"entrance-{suffix}-stage.png"));
        renderer.SetModelVisibleForProbe(playerId, true);
        renderer.SetModelVisibleForProbe(enemyId, true);
        renderer.SetHudVisibleForProbe(true);
        var hudVisible = renderer.EffectiveHudVisibleForProbe;
        if (fade < float.Epsilon)
        {
            var mean = BattlePixelMetrics.MeanLuminance(full);
            QuietLog.Info($"battle-entrance fade={fade:0.00} fighter=0.0 stage=0.0 mean={mean:0.0} hud_visible={hudVisible}");
            return (0, 0, mean, hudVisible);
        }

        var measurements = BattlePixelMetrics.FighterAndStageLuminance(full, stage);
        QuietLog.Info($"battle-entrance fade={fade:0.00} fighter={measurements.Fighter:0.0} stage={measurements.Stage:0.0} mean={measurements.Mean:0.0} hud_visible={hudVisible}");
        return (measurements.Fighter, measurements.Stage, measurements.Mean, hudVisible);
    }
}
