using System.Globalization;
using System.Text.Json;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Plays a running entrance call tick by tick through the production frame coordinator, renderer and physical steps,
/// checks each tick against the source caller and entrance, keeps the post message open long enough to see the camera
/// return finish without a command-camera overwrite, and writes the samples and captures for the lead to read.
/// </summary>
internal sealed class BattleEntranceCallProof
{
    /// <summary>Entrance frame drawn twice, with and without the GTE/GPU projection rules.</summary>
    private const int ProjectionPairFrame = 45;

    /// <summary>Entrance frames captured as images.</summary>
    private static readonly int[] SequenceCaptures = [20, 45, 110, 126, 150];

    private readonly IBattleProbeHost _host;
    private readonly BattleProductionFrameDriver _driver;
    private readonly string _captureRoot;

    /// <summary>Initializes a new instance of the <see cref="BattleEntranceCallProof"/> class.</summary>
    /// <param name="host">Live battle probe host.</param>
    /// <param name="driver">Production frame driver.</param>
    /// <param name="captureRoot">Private capture directory.</param>
    public BattleEntranceCallProof(IBattleProbeHost host, BattleProductionFrameDriver driver, string captureRoot)
    {
        _host = host;
        _driver = driver;
        _captureRoot = captureRoot;
    }

    /// <summary>Plays the running call to its release and checks it.</summary>
    /// <param name="expected">Expected entrance.</param>
    /// <param name="measuredClock">Whether each rendered frame feeds the measured wall time, as Godot does, instead of
    /// exactly one 60 Hz tick.</param>
    /// <returns>The samples of every played frame.</returns>
    public async Task<IReadOnlyList<BattleEntranceTickSample>> RunAsync(BattleEntranceExpectation expected, bool measuredClock = false)
    {
        BattleEntranceCall call = _host.ActorEntranceCall ?? throw new InvalidOperationException($"{expected.Label}: no entrance call started");
        ProbeRequirement.Require(call.Sequence.Enemy == expected.Enemy, $"{expected.Label}: entering side enemy={call.Sequence.Enemy}");
        ProbeRequirement.Require((call.Phase == BattleEntranceCallPhase.Before) == (expected.BeforeKey is not null), $"{expected.Label}: initial phase={call.Phase}");
        _host.SetEntranceConfirmForProbe(() => call.Phase == BattleEntranceCallPhase.After || (call.Phase == BattleEntranceCallPhase.Sequence && call.OpenMessage is not null) ? call.AfterFrames >= expected.ConfirmAfter : null);
        string heldFlow = _host.Flow.Phase.ToString();
        var samples = new List<BattleEntranceTickSample>();
        double startSeconds = _driver.Seconds;
        int startTicks = _driver.Ticks;
        var wall = System.Diagnostics.Stopwatch.StartNew();
        double fed = 0;
        while (samples.Count == 0 || samples[^1].Phase != nameof(BattleEntranceCallPhase.Done))
        {
            ProbeRequirement.Require(samples.Count < 3000, $"{expected.Label}: entrance call did not end within 3000 frames");
            double delta = measuredClock ? wall.Elapsed.TotalSeconds - fed : 1.0 / 60.0;
            fed += delta;
            _driver.Advance(delta);
            var sample = BattleEntranceTickReader.Read(_host, call, _driver.Ticks - startTicks, expected.Enemy);
            CheckFlash(call, sample, expected);
            BattleEntranceTickChecks.Check(sample, expected, heldFlow);
            samples.Add(sample);
            await _host.NextFrameDrawn();
            Capture(sample, expected.Label);
            if (sample.Phase == nameof(BattleEntranceCallPhase.Sequence) && sample.Frame == ProjectionPairFrame)
            {
                await CaptureProjectionPairAsync(expected.Label);
            }
        }

        _host.SetEntranceConfirmForProbe(null);
        CheckEnd(call, samples, expected);
        int cueFrame = CueFrame(samples, expected.Label);
        await File.WriteAllTextAsync(Path.Combine(_captureRoot, $"{expected.Label}-ticks.json"), JsonSerializer.Serialize(samples));
        QuietLog.Info(string.Create(CultureInfo.InvariantCulture, $"battle-entrance-proof {expected.Label} PASS clock={(measuredClock ? "measured" : "fixed")} ticks={_driver.Ticks - startTicks} fed={_driver.Seconds - startSeconds:0.000}s wall={wall.Elapsed.TotalSeconds:0.000}s rendered={samples.Count} dropped={_driver.DroppedTicks} frames={call.Sequence.Frames} message={call.AfterFrames} idle={expected.Idle} cue-frame={cueFrame}"));
        return samples;
    }

    /// <summary>Returns the entrance frame at which the cue played, which must be the source's frame 61 (right before
    /// the flash) whatever the clock: the cue follows the entrance's own clock, not a separate wall-clock delay.</summary>
    /// <param name="samples">Played samples.</param>
    /// <param name="label">Proof label.</param>
    /// <returns>The frame.</returns>
    private static int CueFrame(List<BattleEntranceTickSample> samples, string label)
    {
        int before = samples.First(sample => sample.Phase == nameof(BattleEntranceCallPhase.Sequence)).CuePlays;
        var played = samples.Where(sample => sample.Phase == nameof(BattleEntranceCallPhase.Sequence) && sample.CuePlays > before).ToList();
        int cueFrame = played.Count == 0 ? -1 : played[0].Frame;
        ProbeRequirement.Require(
            cueFrame == BattleEntranceSequence.LeadFrames + 1 && played[^1].CuePlays == before + 1,
            $"{label}: entrance cue played at entrance frame {cueFrame} plays={(played.Count == 0 ? before : played[^1].CuePlays) - before}, the source plays it once at frame 61");
        return cueFrame;
    }

    /// <summary>Checks that the sprite surface draws the entrance's white level.</summary>
    /// <param name="call">Entrance call.</param>
    /// <param name="sample">Observed tick.</param>
    /// <param name="expected">Expected entrance.</param>
    private void CheckFlash(BattleEntranceCall call, BattleEntranceTickSample sample, BattleEntranceExpectation expected)
    {
        if (call.Phase == BattleEntranceCallPhase.Sequence)
        {
            float drawn = BattleEntranceTickReader.DrawnFlash(_host);
            ProbeRequirement.Require(Math.Abs(drawn - (sample.Flash / 255f)) < 0.002f, $"{expected.Label} frame={sample.Frame}: drawn flash={drawn} expected={sample.Flash}");
        }
    }

    /// <summary>Checks the entrance length, the camera return and the incoming idle.</summary>
    /// <param name="call">Finished call.</param>
    /// <param name="samples">Every played tick.</param>
    /// <param name="expected">Expected entrance.</param>
    private void CheckEnd(BattleEntranceCall call, List<BattleEntranceTickSample> samples, BattleEntranceExpectation expected)
    {
        ProbeRequirement.Require(call.Sequence.Frames == expected.SequenceFrames, $"{expected.Label}: entrance frames={call.Sequence.Frames} expected={expected.SequenceFrames}");
        var after = samples.Where(sample => sample.Phase == nameof(BattleEntranceCallPhase.After)).ToList();
        var settled = after.Where(sample => sample.AfterTicks > expected.ReturnFadeFrames + 1).ToList();
        ProbeRequirement.Require(settled.Count > 1, $"{expected.Label}: post message closed after {call.AfterFrames} units");
        ProbeRequirement.Require(settled.All(sample => sample.Camera == settled[0].Camera), $"{expected.Label}: the camera moved after its return fade while the post message was open");
        ProbeRequirement.Require(
            expected.Enemy || after[0].Camera != after.First(sample => sample.AfterTicks >= expected.ReturnFadeFrames / 2).Camera,
            $"{expected.Label}: the player return fade did not move the camera");
        int idle = _host.Renderer.ActorScene.FighterModel(expected.Enemy)?.Motion?.IdleMotion ?? -1;
        ProbeRequirement.Require(idle == expected.Idle, $"{expected.Label}: incoming idle={idle} expected={expected.Idle}");
    }

    /// <summary>Draws the same entrance frame again without the GTE saturation and GPU span rules, as the paired
    /// control of the projection proof, then restores the production rules on that frame. No time passes.</summary>
    /// <param name="label">Proof label.</param>
    /// <returns>A task completed once both renders were captured.</returns>
    private async Task CaptureProjectionPairAsync(string label)
    {
        BattleModelLayer raster = _host.Renderer.RasterForProbe;
        int filtered = raster.GpuRejectedCount;
        int drawn = raster.PolygonCount;
        BattleRasterControl? previous = raster.ProbeControl;
        raster.ProbeControl = new BattleRasterControl { UnfilteredProjection = true };
        _driver.Advance(0);
        await _host.NextFrameDrawn();
        int unfilteredDrawn = raster.PolygonCount;
        Image image = _host.CaptureViewport();
        ProbeRequirement.Require(image.SavePng(Path.Combine(_captureRoot, $"{label}-frame-{ProjectionPairFrame:000}-unfiltered.png")) == Error.Ok, $"{label}: unfiltered capture failed");
        raster.ProbeControl = previous;
        _driver.Advance(0);
        await _host.NextFrameDrawn();
        QuietLog.Info(string.Create(CultureInfo.InvariantCulture, $"battle-entrance-projection {label} frame={ProjectionPairFrame} drawn={drawn} gpu-rejected={filtered} unfiltered-drawn={unfilteredDrawn} restored-drawn={raster.PolygonCount}"));
    }

    /// <summary>Writes the captures of the chosen ticks.</summary>
    /// <param name="sample">Observed tick.</param>
    /// <param name="label">Capture label.</param>
    private void Capture(BattleEntranceTickSample sample, string label)
    {
        string? name = sample.Phase switch
        {
            nameof(BattleEntranceCallPhase.Before) when sample.Tick == 8 => "before",
            nameof(BattleEntranceCallPhase.Sequence) when SequenceCaptures.Contains(sample.Frame) => $"frame-{sample.Frame:000}",
            nameof(BattleEntranceCallPhase.After) when sample.AfterTicks is 1 or 30 or 70 => $"after-{sample.AfterTicks:000}",
            nameof(BattleEntranceCallPhase.Done) => "released",
            _ => null,
        };
        if (name is null)
        {
            return;
        }

        Image image = _host.CaptureViewport();
        ProbeRequirement.Require(image.SavePng(Path.Combine(_captureRoot, $"{label}-{name}.png")) == Error.Ok, $"{label}: capture {name} failed");
    }
}
