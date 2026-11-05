using System.Text.Json;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Replays one existing source vector and isolates its visible child's current Canvas contribution.</summary>
internal static class BattleHiddenParentCanvasProof
{
    /// <summary>Measures only child visibility with source pose, camera, light and all other bones unchanged.</summary>
    /// <param name="host">Production model probe host.</param>
    /// <param name="root">Private capture output.</param>
    /// <returns>A task completed after the paired framebuffer proof.</returns>
    public static async Task RunAsync(IBattleProbeHost host, string root)
    {
        var path = System.Environment.GetEnvironmentVariable("DW3_BATTLE_SKELETON_VECTORS")
            ?? throw new InvalidOperationException("source skeleton vector is required");
        using var data = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var vector = data.RootElement.GetProperty("parent_hidden_child_visible_cases")[0];
        var fighter = vector.GetProperty("fighter").GetInt32();
        var frame = vector.GetProperty("frame").GetInt32();
        var parent = vector.GetProperty("parent").GetInt32();
        var child = vector.GetProperty("child").GetInt32();
        var renderer = host.Renderer;
        renderer.BuildModels(host.ResolveBattleModelPath(fighter), host.ResolveBattleModelPath(383), "source vector", "opponent", 383);
        renderer.StopShotCamera();
        renderer.SetSourceCamera(renderer.FighterCameras(enemy: false)[2], snap: true);
        await BattleProbeCapture.CaptureAsync(host, root, "hidden-parent-settled.png");
        renderer.SetProbeAnimationFrame(0);
        renderer.SetProbePose(frame);
        ProbeRequirement.Require(!renderer.BoneVisible(fighter, parent) && renderer.BoneVisible(fighter, child), "source vector did not establish local hidden-parent/visible-child state");
        var node = renderer.ModelForProbe(fighter).Root.FindChild($"Bone{child}", true, false) as Node3D
            ?? throw new InvalidOperationException("source child node unavailable");
        var reject = System.Environment.GetEnvironmentVariable("DW3_BATTLE_HIDDEN_PARENT_REJECT") == "1";
        node.Visible = !reject;
        var visible = await CaptureAsync(host, root, "hidden-parent-child-visible.png");
        node.Visible = false;
        var hidden = await CaptureAsync(host, root, "hidden-parent-child-suppressed.png");
        node.Visible = !reject;
        var restored = await CaptureAsync(host, root, "hidden-parent-child-restored.png");
        var pixels = BattlePixelMetrics.CountDifferent(visible, hidden, 0, visible.GetWidth());
        var control = BattlePixelMetrics.CountDifferent(visible, restored, 0, visible.GetWidth());
        var bounds = BattlePixelMetrics.DifferenceBoundsAboveThreshold(visible, hidden, 0);
        QuietLog.Info($"battle-hidden-parent-canvas fighter={fighter} frame={frame} parent={parent} child={child} pixels={pixels} restored_diff={control} bounds={bounds} rejected_control={reject}");
        ProbeRequirement.Require(pixels > 0 && control == 0, "hidden-parent visible child made no isolated Canvas framebuffer contribution");
    }

    /// <summary>Rebuilds the current Canvas with zero elapsed time so neither motion nor lighting can explain a difference.</summary>
    /// <param name="host">Live renderer and viewport.</param>
    /// <param name="root">Private capture directory.</param>
    /// <param name="name">Output PNG file name.</param>
    /// <returns>The actual composed framebuffer.</returns>
    private static Task<Image> CaptureAsync(IBattleProbeHost host, string root, string name)
    {
        host.Renderer.Present(host.BuildBattleView(), 0);
        return BattleProbeCapture.CaptureRawAsync(host, root, name);
    }
}
