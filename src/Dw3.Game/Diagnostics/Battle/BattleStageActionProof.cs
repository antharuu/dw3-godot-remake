using System.Text.Json;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Measures stage geometry after initialization with actors, HUD, camera and lighting held fixed.</summary>
internal static class BattleStageActionProof
{
    /// <summary>Checks the active arena and an externally selected animated registry arena on the same runtime path.</summary>
    /// <param name="host">Live production battle.</param>
    /// <param name="captureRoot">Private output directory.</param>
    /// <returns>A task completed after the transform and framebuffer assertions.</returns>
    public static async Task RunAsync(IBattleProbeHost host, string captureRoot)
    {
        var path = System.Environment.GetEnvironmentVariable("DW3_BATTLE_STAGE") ?? host.ResolveBattleStagePath(18);
        await VerifyAsync(host, captureRoot, path, "current");
        var animated = System.Environment.GetEnvironmentVariable("DW3_BATTLE_ACTION_ANIMATED_STAGE")
            ?? throw new InvalidOperationException("An animated registry stage is required for common-runtime proof");
        var id = int.Parse(animated, System.Globalization.CultureInfo.InvariantCulture);
        path = host.ResolveRegisteredStageForProbe(id);
        ProbeRequirement.Require(host.Renderer.BuildStage(path, id), "animated registry stage did not load");
        using var data = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        ProbeRequirement.Require(HasAnimatedTracks(data.RootElement.GetProperty("tracks")), "selected common-runtime stage is static");
        await VerifyAsync(host, captureRoot, path, "animated-registry");
    }

    /// <summary>Ignores timestamps and detects actual changes in decoded position, rotation or scale channels.</summary>
    /// <param name="tracks">Source tracks keyed by bone, with three channels of time and XYZ rows.</param>
    /// <returns>Whether at least one channel changes its geometric values.</returns>
    internal static bool HasAnimatedTracks(JsonElement tracks) => tracks.EnumerateObject()
        .SelectMany(bone => bone.Value.EnumerateArray())
        .Any(channel => channel.EnumerateArray()
            .Select(row => string.Join(',', row.EnumerateArray().Skip(1).Select(value => value.GetInt32())))
            .Distinct(StringComparer.Ordinal).Skip(1).Any());

    /// <summary>Measures a settled arena through ordinary Present calls without posing its skeleton.</summary>
    /// <param name="host">Live battle and renderer.</param>
    /// <param name="root">Private capture directory.</param>
    /// <param name="path">Loaded stage document.</param>
    /// <param name="label">Output prefix, not a motion-state input.</param>
    /// <returns>A task completed once all captured geometry and image checks pass.</returns>
    private static async Task VerifyAsync(IBattleProbeHost host, string root, string path, string label)
    {
        var renderer = host.Renderer;
        renderer.StopShotCamera();
        renderer.ClearModelsForSheet();
        var view = host.BuildBattleView() ?? throw new InvalidOperationException("battle view unavailable");
        view = view with { Phase = BattleFlowPhase.EnemyTurn, Menu = null, MessageVisible = false };
        Advance(renderer, view, 120);
        var before = renderer.CaptureStageMotionForProbe() ?? throw new InvalidOperationException("stage snapshot unavailable");
        ProbeRequirement.Require(renderer.LightFade >= 1f && (before.Blend == 32767 || (!before.Blending && before.Frame > 0)), $"stage initialization did not finish frame={before.Frame} blend={before.Blend} blending={before.Blending}");
        renderer.SetHudVisibleForProbe(false);
        var imageBefore = await BattleProbeCapture.CaptureRawAsync(host, root, $"{label}-stage-before.png");
        Advance(renderer, view, 180);
        var after = renderer.CaptureStageMotionForProbe() ?? throw new InvalidOperationException("stage snapshot disappeared");
        renderer.SetHudVisibleForProbe(false);
        var imageAfter = await BattleProbeCapture.CaptureRawAsync(host, root, $"{label}-stage-after.png");
        using var data = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var animated = HasAnimatedTracks(data.RootElement.GetProperty("tracks"));
        var changed = before.Bones.Zip(after.Bones).Count(pair => pair.First != pair.Second);
        var pixels = BattlePixelMetrics.CountDifferent(imageBefore, imageAfter, 0, imageBefore.GetWidth());
        await File.WriteAllTextAsync(Path.Combine(root, $"{label}-stage-transforms.json"), JsonSerializer.Serialize(new { path, animated, before, after, changed, pixels }));
        ProbeRequirement.Require(animated ? changed > 0 && pixels > 0 : changed == 0 && pixels == 0, $"stage geometry mismatch animated={animated} changed_bones={changed} pixels={pixels}");
        QuietLog.Info($"battle-stage-geometry stage={data.RootElement.GetProperty("stage")} animated={animated} changed_bones={changed} pixels={pixels} before_frame={before.Frame} after_frame={after.Frame} light=1 camera=fixed actors=absent elapsed_present_frames=180");
    }

    /// <summary>Advances only the ordinary presentation scheduler, never a pose override or simulation write.</summary>
    /// <param name="renderer">Production renderer.</param>
    /// <param name="view">Fixed view, not the mutable simulation.</param>
    /// <param name="frames">Number of elapsed 60 Hz presentation intervals.</param>
    private static void Advance(BattleRenderer renderer, BattleView view, int frames)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            renderer.Present(view, 1d / 60d);
        }
    }
}
