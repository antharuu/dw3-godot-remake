using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Compares the live production actor against one private qualified draw snapshot.</summary>
internal sealed class BattleCameraProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattleCameraProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleCameraProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_CAMERA_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var snapshot = new BattleCameraSnapshot(System.Environment.GetEnvironmentVariable("DW3_BATTLE_CAMERA_SNAPSHOT") ?? string.Empty);
        var output = Host.PrepareCaptureDirectory("probe/battle-camera");
        Host.ApplyS200();
        Host.StartDebugBattle();
        Host.TickTestSimulation();
        await SettleLightsAsync();
        var renderer = Host.Renderer;
        renderer.Present(Host.BuildBattleView());
        BuildSnapshotTarget(snapshot);
        renderer.SetProbePose(snapshot.PoseFrame);
        renderer.SetProbeAnimationFrame(0);
        var camera = snapshot.Camera();
        if (System.Environment.GetEnvironmentVariable("DW3_BATTLE_CAMERA_NEGATIVE") == "1")
        {
            camera.RotationY += 512;
        }

        renderer.SetSourceCamera(camera);
        renderer.SetModelVisibleForProbe(snapshot.Fighter, visible: true);
        var pivot = LogicalRootProjection(renderer, snapshot.Fighter);
        await Host.NextFrameDrawn();
        var first = Host.CaptureViewport();
        ProbeRequirement.Require(first.SavePng(Path.Combine(output, "snapshot-full.png")) == Error.Ok, "snapshot capture failed");
        await Host.NextFrameDrawn();
        var repeated = Host.CaptureViewport();
        ProbeRequirement.Require(repeated.GetData().AsSpan().SequenceEqual(first.GetData()), "production untoggled frame drifted");
        ProbeRequirement.Require(repeated.SavePng(Path.Combine(output, "snapshot-repeat.png")) == Error.Ok, "snapshot repeat capture failed");
        renderer.SetModelVisibleForProbe(snapshot.Fighter, visible: false);
        await Host.NextFrameDrawn();
        var withoutMain = Host.CaptureViewport();
        ProbeRequirement.Require(withoutMain.SavePng(Path.Combine(output, "snapshot-without-main.png")) == Error.Ok, "snapshot toggle capture failed");
        var source = Image.LoadFromFile(snapshot.ImagePath("full"));
        ProbeRequirement.Require(source.GetWidth() == 320 && source.GetHeight() == 240, "snapshot source image raster mismatch");
        var comparison = BattleComparisonImage.SideBySide(BattleComparisonImage.ToSourceSize(first), source, "REMAKE QUALIFIED VIEW", "SOURCE HELD DRAW");
        ProbeRequirement.Require(comparison.SavePng(Path.Combine(output, "compare-qualified-snapshot.png")) == Error.Ok, "snapshot comparison capture failed");
        var expected = snapshot.ProjectedRoot;
        var delta = pivot.DistanceTo(new Vector2(expected.X, expected.Y));
        QuietLog.Info($"battle-camera-snapshot actor={snapshot.Fighter} pose={snapshot.PoseFrame} source-root=({expected.X:0.00},{expected.Y:0.00}) remake-root=({pivot.X:0.00},{pivot.Y:0.00}) delta={delta:0.00} camera-conversion=live-C-R-C logical=320x240");
        VerifyPhysicalView(snapshot);
        ProbeRequirement.Require(delta <= 1f, $"qualified snapshot camera/root mismatch delta={delta:0.00}");
        VerifyRootAndPose(snapshot);
        VerifySilhouette(first, withoutMain, snapshot);
        if (!snapshot.Enemy && snapshot.Fighter == Host.RenderedPlayerId)
        {
            await VerifyShotTimelineAsync(output);
        }
    }

    /// <summary>Maps the camera projection to the actual logical battle raster.</summary>
    /// <param name="renderer">Active production renderer.</param>
    /// <param name="fighter">Actor whose real root is measured.</param>
    /// <returns>The root in source-raster pixels.</returns>
    private static Vector2 LogicalRootProjection(BattleRenderer renderer, int fighter)
    {
        var camera = renderer.CameraForProbe;
        var size = camera.GetViewport().GetVisibleRect().Size;
        var point = camera.UnprojectPosition(renderer.ModelForProbe(fighter).Root.GlobalPosition);
        var scale = size.Y / 240f;
        return new Vector2((point.X - ((size.X - (320f * scale)) / 2f)) / scale, point.Y / scale);
    }

    /// <summary>Checks each visible bone origin and all three unit-axis endpoints against the draw snapshot.</summary>
    /// <param name="model">Production model with its real camp transform.</param>
    /// <param name="bone">Source bone index.</param>
    /// <param name="world">Observed source world matrix.</param>
    /// <returns>Largest endpoint error in source units.</returns>
    private static float BoneTransformError(BattleModel model, int bone, int[] world)
    {
        var points = new[] { Vector3.Zero, Vector3.Right, Vector3.Down, Vector3.Forward };
        var maximum = 0f;
        for (var axis = 0; axis < points.Length; axis++)
        {
            var delta = axis == 0 ? Vector3.Zero : new Vector3(world[axis - 1], world[axis + 2], world[axis + 5]) / 16f;
            var source = new Vector3(world[9], world[10], world[11]) + delta;
            var expected = new Vector3(source.X, -source.Y, -source.Z) / BattleSceneMath.Ps1UnitsPerGodotUnit;
            maximum = Math.Max(maximum, model.WorldPosition(bone, points[axis]).DistanceTo(expected) * BattleSceneMath.Ps1UnitsPerGodotUnit);
        }

        return maximum;
    }

    /// <summary>Preserves the earlier bounds tolerance using genuinely isolated source and production actors.</summary>
    /// <param name="full">Held production frame.</param>
    /// <param name="withoutMain">Same frame with only the target main layer hidden.</param>
    /// <param name="snapshot">Qualified source image bundle.</param>
    private static void VerifySilhouette(Image full, Image withoutMain, BattleCameraSnapshot snapshot)
    {
        var remake = BattlePixelMetrics.DifferenceBounds(BattleComparisonImage.ToSourceSize(full), BattleComparisonImage.ToSourceSize(withoutMain));
        var source = BattlePixelMetrics.DifferenceBounds(Image.LoadFromFile(snapshot.ImagePath("full")), Image.LoadFromFile(snapshot.ImagePath("without_main")));
        ProbeRequirement.Require(remake is not null && source is not null, "qualified actor silhouette is absent");
        var actual = remake.GetValueOrDefault();
        var expected = source.GetValueOrDefault();
        var delta = Math.Max(Math.Max(Math.Abs(actual.Position.X - expected.Position.X), Math.Abs(actual.Position.Y - expected.Position.Y)), Math.Max(Math.Abs(actual.Size.X - expected.Size.X), Math.Abs(actual.Size.Y - expected.Size.Y)));
        QuietLog.Info($"battle-camera-silhouette actor={snapshot.Fighter} remake={BattleComparisonImage.FormatBounds(actual)} source={BattleComparisonImage.FormatBounds(expected)} delta={delta:0.0}");
        ProbeRequirement.Require(delta <= 8f, "qualified actor bounds exceed eight source pixels");
    }

    /// <summary>Compares physical view origin and directions, not raw authored camera fields.</summary>
    /// <param name="snapshot">Independent source draw matrix.</param>
    private void VerifyPhysicalView(BattleCameraSnapshot snapshot)
    {
        var camera = Host.Renderer.CameraForProbe;
        var origin = snapshot.DrawCameraOrigin();
        var actual = camera.GlobalPosition;
        var positionError = actual.DistanceTo(new Vector3(origin.X, origin.Y, origin.Z)) * BattleSceneMath.Ps1UnitsPerGodotUnit;
        var directions = new[] { camera.GlobalBasis.X, -camera.GlobalBasis.Y, -camera.GlobalBasis.Z };
        var minimumDot = 1f;
        for (var row = 0; row < directions.Length; row++)
        {
            var expected = snapshot.DrawViewDirection(row);
            minimumDot = Math.Min(minimumDot, directions[row].Normalized().Dot(new Vector3(expected.X, expected.Y, expected.Z)));
        }

        QuietLog.Info($"battle-camera-physical origin-error-source-units={positionError:0.00} minimum-direction-dot={minimumDot:0.000000}");
        ProbeRequirement.Require(positionError <= 8f && minimumDot >= 0.99999f, "physical camera differs from observed draw transform");
    }

    /// <summary>Uses prepared models at their natural camps for additional qualified vectors.</summary>
    /// <param name="snapshot">Actor and camp to observe.</param>
    private void BuildSnapshotTarget(BattleCameraSnapshot snapshot)
    {
        var player = snapshot.Enemy ? Host.RenderedPlayerId : snapshot.Fighter;
        var activeEnemy = Host.Flow.State.ActiveEnemyFighter?.Id ?? throw new InvalidOperationException("snapshot has no active enemy");
        var enemy = snapshot.Enemy ? snapshot.Fighter : activeEnemy;
        if (player == Host.RenderedPlayerId && enemy == activeEnemy)
        {
            return;
        }

        Host.Renderer.BuildModels(Host.ResolveBattleModelPath(player), Host.ResolveBattleModelPath(enemy), "snapshot-player", "snapshot-enemy", enemy);
        Host.Renderer.SetLightFadeForProbe(1f);
    }

    /// <summary>Checks the actual root and independent source bone origins without moving the production model.</summary>
    /// <param name="snapshot">Qualified source draw snapshot.</param>
    private void VerifyRootAndPose(BattleCameraSnapshot snapshot)
    {
        var model = Host.Renderer.ModelForProbe(snapshot.Fighter);
        var root = snapshot.Root;
        var expected = new Vector3(root.X, -root.Y, -root.Z) / BattleSceneMath.Ps1UnitsPerGodotUnit;
        ProbeRequirement.Require(model.Root.GlobalPosition.DistanceTo(expected) < 0.001f, "production actor root differs from captured root");
        var bone = 0;
        var maximum = 0f;
        var visibility = snapshot.BoneVisibility.ToArray();
        ProbeRequirement.Require(visibility.Length == model.BoneCount, "snapshot visibility count mismatch");
        foreach (var world in snapshot.Bones)
        {
            ProbeRequirement.Require(world.Length == 12, "snapshot bone matrix is incomplete");
            var position = new Vector3(world[9], -world[10], -world[11]) / BattleSceneMath.Ps1UnitsPerGodotUnit;
            ProbeRequirement.Require(model.IsBoneVisible(bone) == visibility[bone], $"snapshot bone visibility mismatch: {bone}");
            if (!visibility[bone])
            {
                bone++;
                continue;
            }

            var actual = model.WorldPosition(bone, Vector3.Zero);
            var error = BoneTransformError(model, bone, world);
            QuietLog.Info($"battle-camera-snapshot bone={bone} expected={position} actual={actual} error={error:0.00}");
            bone++;
            maximum = Math.Max(maximum, error);
        }

        QuietLog.Info($"battle-camera-snapshot pose-bones={bone} maximum-origin-error-source-units={maximum:0.00}");
        ProbeRequirement.Require(bone == model.BoneCount && maximum <= 8f, "production pose differs from captured bone origins");
    }

    /// <summary>Advances the production entrance fade before holding the snapshot.</summary>
    /// <returns>A task that completes after the last rendered frame.</returns>
    private async Task SettleLightsAsync()
    {
        while (Host.Renderer.LightFade < 1f)
        {
            Host.Renderer.Present(Host.BuildBattleView());
            await Host.NextFrameDrawn();
        }
    }

    /// <summary>Retains the earlier menu-shot visibility check after the qualified pair passes.</summary>
    /// <param name="output">Capture directory.</param>
    /// <returns>A task that completes after the existing timeline landmarks are drawn.</returns>
    private async Task VerifyShotTimelineAsync(string output)
    {
        var renderer = Host.Renderer;
        renderer.SetCommandMenuCamera();
        renderer.SetProbePose(0);
        for (var frame = 0; frame <= 200; frame++)
        {
            renderer.Present(Host.BuildBattleView());
            if (frame == 120)
            {
                ProbeRequirement.Require(renderer.ShotCameraSummary().Contains("enemy=False", StringComparison.Ordinal), "substate 5 did not hide the enemy model");
            }

            if (frame is 0 or 60 or 120 or 200)
            {
                await Host.NextFrameDrawn();
                ProbeRequirement.Require(Host.CaptureViewport().SavePng(Path.Combine(output, $"shot-t{frame}.png")) == Error.Ok, "shot timeline capture failed");
                QuietLog.Info($"battle-shot-camera t={frame} {renderer.ShotCameraSummary()}");
            }
        }
    }
}
