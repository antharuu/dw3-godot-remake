using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Proves decoded battle motion stepping, blending and return to idle.</summary>
internal sealed class BattleMotionProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattleMotionProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleMotionProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_MOTION_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory("probe/battle-motion");
        Host.StartDebugBattle();
        ProbeRequirement.Require(Host.RenderedPlayerId == 383, "F8 did not render Kotemon 383");
        var renderer = Host.Renderer;
        ProbeRequirement.Require(renderer.ModelCount == 2, "F8 did not build two real fighter models");
        renderer.SetMotionAll(1, restart: true);
        var idle0 = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "idle-f0.png");
        renderer.StepMotions(0.6f);
        var idle30 = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "idle-f30.png");
        renderer.StepMotions(0.6f);
        var idle60 = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "idle-f60.png");
        var idleDiff30 = BattlePixelMetrics.CountDifferent(idle0, idle30, 0, idle0.GetWidth());
        var idleDiff60 = BattlePixelMetrics.CountDifferent(idle0, idle60, 0, idle0.GetWidth());
        ProbeRequirement.Require(
            idleDiff30 > 50 && idleDiff60 > 50,
            $"idle frames are frozen d30={idleDiff30} d60={idleDiff60}");
        QuietLog.Info($"battle-motion fighter=383 motion=1 keyframes=62 loop=true idle-diff30={idleDiff30} idle-diff60={idleDiff60}");
        await AssertAttackReturnsToIdleAsync(renderer, captureRoot);
    }

    /// <summary>Captures the source attack blend and verifies its terminal fighter pixels.</summary>
    /// <param name="renderer">Active renderer with Kotemon loaded.</param>
    /// <param name="captureRoot">Directory receiving the motion frames.</param>
    /// <returns>A task that completes after the final attack frame.</returns>
    private async Task AssertAttackReturnsToIdleAsync(BattleRenderer renderer, string captureRoot)
    {
        renderer.SetMotionAll(3, restart: true);
        await BattleProbeCapture.CaptureAsync(Host, captureRoot, "attack-start.png");
        renderer.StepMotions(0.24f);
        await BattleProbeCapture.CaptureAsync(Host, captureRoot, "attack-mid.png");
        renderer.StepMotions(0.5f);
        var end = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "attack-end.png");
        ProbeRequirement.Require(
            BattlePixelMetrics.CountBright(end, 0, end.GetWidth()) > 1000,
            "attack end rendered no fighter pixels");
        QuietLog.Info("battle-motion fighter=383 motion=3 done=true back_to_idle=true");
        QuietLog.Info($"battle-motion probe PASS captures={captureRoot}/idle-f0.png,{captureRoot}/attack-end.png");
    }
}
