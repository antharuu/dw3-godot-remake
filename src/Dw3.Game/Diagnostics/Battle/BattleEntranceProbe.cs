using System.Globalization;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Separates natural battle restarts from controlled entrance-fraction measurements.</summary>
internal sealed class BattleEntranceProbe : BattleProbe
{
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
        var root = Host.PrepareCaptureDirectory("probe/battle-entrance");
        Host.ApplyS200();
        await CaptureNaturalEntranceAsync(root, "first");
        await CaptureNaturalEntranceAsync(root, "repeat");
        await CaptureControlledFractionsAsync(root);
        QuietLog.Info("battle-entrance PASS natural-reset and controlled-fractions");
    }

    /// <summary>Uses the real start, simulation and presentation paths, without supplying a fade reset.</summary>
    /// <param name="root">Private evidence directory.</param>
    /// <param name="label">First or repeated start.</param>
    /// <returns>A task completed after the natural entrance reaches command-ready light.</returns>
    private async Task CaptureNaturalEntranceAsync(string root, string label)
    {
        Host.StartDebugBattle();
        var renderer = Host.Renderer;
        ProbeRequirement.Require(renderer.LightFade == 0f, "normal start did not reset fade");
        Host.TickTestSimulation();
        var oracle = new BattleEntranceOracle(Host, root);
        for (var frame = 1; frame <= 74; frame++)
        {
            // The test driver owns _Process, so explicitly execute the ordinary presentation before drawing.
            // Skipping it deliberately recreates the old probe's retained, unpresented Canvas mesh.
            var bypass = label == "repeat" && System.Environment.GetEnvironmentVariable("DW3_BATTLE_ENTRANCE_BYPASS_PRESENT") == "1";
            if (!bypass)
            {
                renderer.Present(Host.BuildBattleView(), 1d / 60d);
            }

            if (frame == 1)
            {
                InjectBrightNegative();
                var first = await oracle.CaptureAsync($"natural-{label}-first.png");
                QuietLog.Info($"battle-entrance natural={label} presented_frame={frame} fade={renderer.LightFade:0.000000} phase={Host.Flow.Phase}");
                oracle.RequireBlack(first);
            }
            else if (frame == 36 || renderer.LightFade >= 1f)
            {
                QuietLog.Info($"battle-entrance natural={label} presented_frame={frame} fade={renderer.LightFade:0.000000} phase={Host.Flow.Phase}");
                await oracle.VerifyOverlaysAsync($"natural-{label}-{frame:00}");
            }
            else
            {
                await Host.NextFrameDrawn();
            }

            if (renderer.LightFade >= 1f)
            {
                ProbeRequirement.Require(frame > 36, "natural entrance skipped intermediate lighting");
                return;
            }
        }

        throw new InvalidOperationException("natural entrance never completed");
    }

    /// <summary>Mutates actual fighter lighting, never image pixels, to prove localized rejection.</summary>
    private void InjectBrightNegative()
    {
        if (System.Environment.GetEnvironmentVariable("DW3_BATTLE_ENTRANCE_RETAIN_BRIGHT") != "1")
        {
            return;
        }

        var renderer = Host.Renderer;
        var models = new[]
        {
            renderer.ModelForProbe(Host.Flow.State.ActivePlayerFighter!.Id),
            renderer.ModelForProbe(Host.Flow.State.ActiveEnemyFighter!.Id),
        };
        foreach (var model in models)
        {
            model.SetLighting(new BattleModel.BattleLightSet([], Colors.White));
        }

        renderer.RasterForProbe.Rebuild(renderer.CameraForProbe, models);
    }

    /// <summary>Preserves the five original samples, presenting each explicitly without advancing its fraction.</summary>
    /// <param name="root">Private evidence directory.</param>
    /// <returns>A task completed after all actor and overlay measurements pass.</returns>
    private async Task CaptureControlledFractionsAsync(string root)
    {
        var renderer = Host.Renderer;
        renderer.SetProbePose(10);
        renderer.SetProbeAnimationFrame(0);
        var oracle = new BattleEntranceOracle(Host, root);
        double previous = 0;
        foreach (var fade in new[] { 0f, .25f, .5f, .75f, 1f })
        {
            renderer.SetLightFadeForProbe(fade);
            renderer.Present(Host.BuildBattleView(), 0d);
            var suffix = fade.ToString("0.00", CultureInfo.InvariantCulture);
            var image = await oracle.VerifyOverlaysAsync($"entrance-{suffix}");
            ProbeRequirement.Require(Math.Abs(renderer.LightFade - fade) < float.Epsilon, "controlled capture advanced the fade");
            if (fade == 0f)
            {
                oracle.RequireBlack(image);
            }

            var mean = await oracle.MeasureActorsAsync(image, $"entrance-{suffix}");
            ProbeRequirement.Require(mean >= previous, $"fighter lighting regressed fade={fade}");
            previous = mean;
        }
    }
}
