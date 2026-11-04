using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Proves decoded battle motion playback and equal-time PAL cadence through the production renderer.</summary>
internal sealed class BattleMotionProbe : BattleProbe
{
    private static readonly int[] Cadences = [30, 60, 120];

    private static readonly int[] Fighters = [383, 51];

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
        var trials = RunCadenceTrials(renderer);
        BattleMotionCadenceVerifier.WriteEvidence(captureRoot, trials);
        BattleMotionCadenceVerifier.AssertEquivalent(trials, Cadences, Fighters);
        await AssertRenderedMotionAsync(renderer, captureRoot);
    }

    /// <summary>Runs the same interrupted-and-returning motion sequence at every presentation cadence.</summary>
    /// <param name="renderer">Live production renderer advanced only through <see cref="BattleRenderer.Present(BattleView?, double)"/>.</param>
    /// <returns>Cadence-indexed traces for both real priority actors.</returns>
    private Dictionary<int, IReadOnlyDictionary<int, BattleMotionCadenceTrace>> RunCadenceTrials(BattleRenderer renderer)
    {
        var trials = new Dictionary<int, IReadOnlyDictionary<int, BattleMotionCadenceTrace>>();
        foreach (var cadence in Cadences)
        {
            trials.Add(cadence, RunCadenceTrial(renderer, cadence));
        }

        return trials;
    }

    /// <summary>Runs one production trace: attack blend, a real interruption to idle, then attack completion and idle return.</summary>
    /// <param name="renderer">Live production renderer.</param>
    /// <param name="cadence">Presentation frames per second.</param>
    /// <returns>Actor traces sampled at eight identical ten-PAL-frame checkpoints.</returns>
    private Dictionary<int, BattleMotionCadenceTrace> RunCadenceTrial(BattleRenderer renderer, int cadence)
    {
        renderer.ResetMotionClockForProbe();
        renderer.SetMotionAll(3, restart: true);
        var samples = Fighters.ToDictionary(fighter => fighter, _ => new List<BattleMotionCadenceSample>());
        var sourceFrame = 0;
        sourceFrame += 10;
        AdvanceSegment(renderer, cadence, sourceFrame, samples);
        renderer.SetMotionAll(1, restart: true);
        sourceFrame += 10;
        AdvanceSegment(renderer, cadence, sourceFrame, samples);
        renderer.SetMotionAll(3, restart: true);
        for (var segment = 0; segment < 6; segment++)
        {
            sourceFrame += 10;
            AdvanceSegment(renderer, cadence, sourceFrame, samples);
        }

        return samples.ToDictionary(
            pair => pair.Key,
            pair => new BattleMotionCadenceTrace(cadence.ToString(System.Globalization.CultureInfo.InvariantCulture), pair.Value));
    }

    /// <summary>Presents a fixed 0.2-second segment, then records live machine publications and skeleton transforms.</summary>
    /// <param name="renderer">Live production renderer.</param>
    /// <param name="cadence">Presentation frames per second.</param>
    /// <param name="sourceFrame">Expected elapsed PAL source frame at the segment boundary.</param>
    /// <param name="samples">Per-actor trace collection receiving the live snapshots.</param>
    private void AdvanceSegment(BattleRenderer renderer, int cadence, int sourceFrame, Dictionary<int, List<BattleMotionCadenceSample>> samples)
    {
        for (var presentationFrame = 0; presentationFrame < cadence / 5; presentationFrame++)
        {
            renderer.Present(Host.BuildBattleView(), 1d / cadence);
        }

        foreach (var fighter in Fighters)
        {
            samples[fighter].Add(renderer.ModelForProbe(fighter).CaptureCadenceSample(sourceFrame));
        }
    }

    /// <summary>Captures the source attack blend after the rate-equivalence trace has completed.</summary>
    /// <param name="renderer">Active renderer with Kotemon loaded.</param>
    /// <param name="captureRoot">Directory receiving the motion frames.</param>
    /// <returns>A task that completes after the final attack frame.</returns>
    private async Task AssertRenderedMotionAsync(BattleRenderer renderer, string captureRoot)
    {
        renderer.SetMotionAll(1, restart: true);
        var idle0 = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "idle-f0.png");
        renderer.StepMotions(0.6f);
        var idle30 = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "idle-f30.png");
        renderer.StepMotions(0.6f);
        var idle60 = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "idle-f60.png");
        var idleDiff30 = BattlePixelMetrics.CountDifferent(idle0, idle30, 0, idle0.GetWidth());
        var idleDiff60 = BattlePixelMetrics.CountDifferent(idle0, idle60, 0, idle0.GetWidth());
        ProbeRequirement.Require(idleDiff30 > 50 && idleDiff60 > 50, $"idle frames are frozen d30={idleDiff30} d60={idleDiff60}");
        renderer.SetMotionAll(3, restart: true);
        await BattleProbeCapture.CaptureAsync(Host, captureRoot, "attack-start.png");
        renderer.StepMotions(0.24f);
        await BattleProbeCapture.CaptureAsync(Host, captureRoot, "attack-mid.png");
        renderer.StepMotions(0.5f);
        var end = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "attack-end.png");
        ProbeRequirement.Require(BattlePixelMetrics.CountBright(end, 0, end.GetWidth()) > 1000, "attack end rendered no fighter pixels");
        QuietLog.Info($"battle-motion rendered PASS idle-diff30={idleDiff30} idle-diff60={idleDiff60} captures={captureRoot}/attack-end.png");
    }
}
