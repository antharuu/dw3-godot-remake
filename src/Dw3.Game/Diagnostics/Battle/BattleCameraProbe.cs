using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Checks source shot-camera landmarks and Kotemon command-frame bounds.</summary>
internal sealed class BattleCameraProbe : BattleProbe
{
    private string _captureRoot = string.Empty;

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
        _captureRoot = Host.PrepareCaptureDirectory("probe/battle-camera");
        Host.ApplyS200();
        Host.StartDebugBattle();
        Host.TickTestSimulation();
        await SettleLightsAsync();
        var renderer = Host.Renderer;
        renderer.SetCommandMenuCamera();
        renderer.SetProbePose(0);
        var first = await CaptureAtAsync(0, "shot-t0.png");
        PresentFrames(1, 60);
        await CaptureAtAsync(60, "shot-t60.png");
        PresentFrames(61, 120);
        await CaptureAtAsync(120, "shot-t120.png");
        ProbeRequirement.Require(
            renderer.ShotCameraSummary().Contains("enemy=False", StringComparison.Ordinal),
            "substate 5 did not hide the enemy model");
        PresentFrames(121, 200);
        await CaptureAtAsync(200, "shot-t200.png");
        await AssertKotemonBoundsAsync(first);
    }

    /// <summary>Advances presentation until the entrance light reaches its settled value.</summary>
    /// <returns>A task that completes after the last rendered frame.</returns>
    private async Task SettleLightsAsync()
    {
        while (Host.Renderer.LightFade < 1f)
        {
            Host.Renderer.Present(Host.BuildBattleView());
            await Host.NextFrameDrawn();
        }
    }

    /// <summary>Presents each source frame in an inclusive-exclusive interval.</summary>
    /// <param name="first">First source frame.</param>
    /// <param name="end">Exclusive end frame.</param>
    private void PresentFrames(int first, int end)
    {
        for (var frame = first; frame < end; frame++)
        {
            Host.Renderer.Present(Host.BuildBattleView());
        }
    }

    /// <summary>Captures one shot-camera frame and records its state-machine summary.</summary>
    /// <param name="frame">Source frame number used in the log.</param>
    /// <param name="name">Output PNG name.</param>
    /// <returns>The captured viewport image.</returns>
    private async Task<Image> CaptureAtAsync(int frame, string name)
    {
        Host.Renderer.Present(Host.BuildBattleView());
        await Host.NextFrameDrawn();
        var image = Host.CaptureViewport();
        ProbeRequirement.Require(
            image.SavePng(Path.Combine(_captureRoot, name)) == Error.Ok,
            $"shot capture write failed={name}");
        QuietLog.Info($"battle-shot-camera t={frame} {Host.Renderer.ShotCameraSummary()}");
        return image;
    }

    /// <summary>Compares remake and source Kotemon bounds at command-menu frame zero.</summary>
    /// <param name="first">Remake command-menu viewport frame.</param>
    /// <returns>A task that completes after the hidden-model comparison frame is drawn.</returns>
    private async Task AssertKotemonBoundsAsync(Image first)
    {
        var renderer = Host.Renderer;
        renderer.SetCommandMenuCamera();
        renderer.Present(Host.BuildBattleView());
        renderer.SetModelVisibleForProbe(383, false);
        await Host.NextFrameDrawn();
        var playerOff = Host.CaptureViewport();
        ProbeRequirement.Require(
            playerOff.SavePng(Path.Combine(_captureRoot, "shot-t0-without-kotemon.png")) == Error.Ok,
            "shot camera player-off write failed");
        renderer.SetModelVisibleForProbe(383, true);
        var remake = BattleComparisonImage.ToSourceSize(first);
        var remakeBox = BattlePixelMetrics.DifferenceBounds(remake, BattleComparisonImage.ToSourceSize(playerOff));
        var source = BattleComparisonImage.ToSourceSize(Host.LoadPsxReference("battle/kotemon-383-command-menu-0.png"));
        var sourceBox = BattlePixelMetrics.WarmKotemonBounds(source);
        ProbeRequirement.Require(remakeBox is not null && sourceBox is not null, $"shot camera bbox unavailable remake={remakeBox} psx={sourceBox}");
        SaveAndAssertBounds(remake, source, remakeBox.GetValueOrDefault(), sourceBox.GetValueOrDefault());
    }

    /// <summary>Saves the paired frame and enforces the eight-source-pixel bounds tolerance.</summary>
    /// <param name="remake">Normalized remake frame.</param>
    /// <param name="source">Normalized source frame.</param>
    /// <param name="remakeBox">Measured remake bounds.</param>
    /// <param name="sourceBox">Measured source bounds.</param>
    private void SaveAndAssertBounds(Image remake, Image source, Rect2 remakeBox, Rect2 sourceBox)
    {
        var delta = Math.Max(
            Math.Max(Math.Abs(remakeBox.Position.X - sourceBox.Position.X), Math.Abs(remakeBox.Position.Y - sourceBox.Position.Y)),
            Math.Max(Math.Abs(remakeBox.Size.X - sourceBox.Size.X), Math.Abs(remakeBox.Size.Y - sourceBox.Size.Y)));
        var comparison = BattleComparisonImage.SideBySide(remake, source, "REMAKE SHOT t0", "PSX COMMAND");
        ProbeRequirement.Require(
            comparison.SavePng(Path.Combine(_captureRoot, "compare-shot-camera-t0.png")) == Error.Ok,
            "shot camera comparison write failed");
        ProbeRequirement.Require(
            delta <= 8,
            $"shot camera Kotemon bbox delta={delta:0} exceeds 8 source pixels remake={BattleComparisonImage.FormatBounds(remakeBox)} psx={BattleComparisonImage.FormatBounds(sourceBox)}");
        QuietLog.Info($"battle-shot-camera PASS bbox remake={BattleComparisonImage.FormatBounds(remakeBox)} psx={BattleComparisonImage.FormatBounds(sourceBox)} delta={delta:0} captures={_captureRoot}; PSX command-menu-1/2 timing is not journaled by the harness, so only -0 is frame-paired");
    }
}
