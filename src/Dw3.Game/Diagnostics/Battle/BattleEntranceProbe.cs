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
        renderer.SetHudVisibleForProbe(false);
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
                continue;
            }

            VerifyFadeProgress(fade, measurements, previousFighter, previousStage);
            previousFighter = measurements.Fighter;
            previousStage = measurements.Stage;
        }

        renderer.SetHudVisibleForProbe(true);
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
        (double Fighter, double Stage, double Mean) current,
        double? previousFighter,
        double? previousStage)
    {
        if (previousFighter is not null && previousStage is not null)
        {
            ProbeRequirement.Require(current.Fighter >= previousFighter, $"fighter darkened while entering fade={fade:0.00}");
            ProbeRequirement.Require(current.Stage >= previousStage, $"stage darkened while entering fade={fade:0.00}");
        }
    }

    /// <summary>Captures the full and stage-only frames for one explicit fade value.</summary>
    /// <param name="captureRoot">Directory receiving both frames.</param>
    /// <param name="fade">Light interpolation in 0..1.</param>
    /// <param name="playerId">Player fighter model id.</param>
    /// <param name="enemyId">Enemy fighter model id.</param>
    /// <returns>Fighter, stage and full-frame luminance.</returns>
    private async Task<(double Fighter, double Stage, double Mean)> CaptureFadeAsync(
        string captureRoot,
        float fade,
        int playerId,
        int enemyId)
    {
        var renderer = Host.Renderer;
        renderer.SetLightFadeForProbe(fade);
        var suffix = fade.ToString("0.00", CultureInfo.InvariantCulture);
        QuietLog.Info($"battle-entrance frame effective_fraction={renderer.LightFade:0.00} pose=idle:{FixedPoseFrame} time=fixed");
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
        if (fade < float.Epsilon)
        {
            var mean = BattlePixelMetrics.MeanLuminance(full);
            QuietLog.Info($"battle-entrance fade={fade:0.00} fighter=0.0 stage=0.0 mean={mean:0.0}");
            return (0, 0, mean);
        }

        var measurements = BattlePixelMetrics.FighterAndStageLuminance(full, stage);
        QuietLog.Info($"battle-entrance fade={fade:0.00} fighter={measurements.Fighter:0.0} stage={measurements.Stage:0.0} mean={measurements.Mean:0.0}");
        return measurements;
    }
}
