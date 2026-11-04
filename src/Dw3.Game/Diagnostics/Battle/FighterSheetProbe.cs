using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Renders every decoded fighter through the production battle renderer into an offline diagnostic
/// sheet. One FIGHTER_SHEET line is emitted per source manifest and failures stay explicit records.
/// </summary>
internal sealed class FighterSheetProbe : BattleProbe
{
    private const int SheetStage = 18;

    /// <summary>
    /// View zero is the source opening shot for both FighterInfo layouts. The old sheet used view 6,
    /// a partner turn close-up and an enemy-facing reverse shot.
    /// </summary>
    private const int ViewIndex = 0;

    private static readonly float[] PullBackFactors = [1f, 1.3f, 1.7f, 2.2f, 3f, 4f, 6f];

    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private readonly List<FighterSheetResult> _results = [];
    private int _failed;

    /// <summary>Initializes a new instance of the <see cref="FighterSheetProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public FighterSheetProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "FIGHTER_SHEET";

    /// <summary>Gets the verdict: any per-fighter failure fails the sheet without aborting it.</summary>
    protected override int CompletedExitCode => _failed == 0 ? 0 : 3;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory("probe/fighter-sheet");
        var catalog = FighterSheetCatalog.Discover(Host.BattleModelsDirectory);
        Host.StartDebugBattle();
        var renderer = Host.HasBattleRenderer ? Host.Renderer : throw new InvalidOperationException("F8 did not construct BattleRenderer");
        await RenderStageBackgroundAsync(renderer, captureRoot);
        _failed = 0;
        _results.Clear();
        foreach (var entry in catalog)
        {
            await RenderRecordedAsync(renderer, captureRoot, entry);
        }

        var reportPath = Path.Combine(captureRoot, "fighter-catalog.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(_results, ReportJsonOptions));
        QuietLog.Info($"FIGHTER_SHEET complete total={catalog.Count} failed={_failed} report={reportPath} root={captureRoot}");
    }

    /// <summary>Moves a source camera away from its reference point.</summary>
    /// <param name="view">Source camera view.</param>
    /// <param name="factor">Distance multiplier, 1 keeps the source view.</param>
    /// <returns>The pulled-back view.</returns>
    private static BattleModel.BattleCameraView PullBack(BattleModel.BattleCameraView view, float factor)
    {
        // The factors never go below 1, so 1 (the smallest) is the source view itself.
        if (factor <= 1f)
        {
            return view;
        }

        var pos = view.CamPos;
        var reference = view.CamRef;
        return new BattleModel.BattleCameraView(
            [
                reference[0] + Mathf.RoundToInt((pos[0] - reference[0]) * factor),
                reference[1] + Mathf.RoundToInt((pos[1] - reference[1]) * factor),
                reference[2] + Mathf.RoundToInt((pos[2] - reference[2]) * factor),
            ],
            reference,
            view.Proj);
    }

    /// <summary>Saves one sheet image or fails with the stable message.</summary>
    /// <param name="image">Image to save.</param>
    /// <param name="path">Output PNG path.</param>
    /// <param name="failure">Failure message.</param>
    private static void Save(Image image, string path, string failure) =>
        ProbeRequirement.Require(image.SavePng(path) == Error.Ok, failure);

    /// <summary>Renders the empty stage once as the sheet's reference background.</summary>
    /// <param name="renderer">Active battle renderer.</param>
    /// <param name="captureRoot">Directory receiving the sheet.</param>
    /// <returns>A task completed after the background is saved.</returns>
    private async Task RenderStageBackgroundAsync(BattleRenderer renderer, string captureRoot)
    {
        if (!renderer.StageBuilt)
        {
            renderer.BuildStage(Host.ResolveBattleStagePath(SheetStage), SheetStage);
        }

        renderer.Show();
        renderer.SetLightFadeForProbe(1f);
        renderer.ClearModelsForSheet();
        var background = await CaptureSourceSizeAsync();
        Save(background, Path.Combine(captureRoot, "stage18-background.png"), "sheet stage background save failed");
    }

    /// <summary>Renders one fighter and records a failure line instead of aborting the sheet.</summary>
    /// <param name="renderer">Active battle renderer.</param>
    /// <param name="captureRoot">Directory receiving the sheet.</param>
    /// <param name="entry">Pack-derived manifest and static preflight record.</param>
    /// <returns>A task completed after the fighter is rendered or recorded as failed.</returns>
    private async Task RenderRecordedAsync(BattleRenderer renderer, string captureRoot, FighterSheetCatalogEntry entry)
    {
        if (!entry.CanRender)
        {
            _failed++;
            _results.Add(new FighterSheetResult(entry.FighterId, entry.IsEnemy, entry.CameraCount, entry.Failures, null, "static preflight"));
            QuietLog.Info($"FIGHTER_SHEET fighter={entry.FighterId} status=preflight failures={string.Join(',', entry.Failures)}");
            return;
        }

        try
        {
            var framing = await RenderFighterAsync(renderer, captureRoot, new SheetFighter(entry.Path, entry.FighterId, entry.IsEnemy) { CameraCount = entry.CameraCount });
            var failures = framing == "source-view-0" ? [] : new[] { FighterSheetFailure.DiagnosticPullBack };
            _results.Add(new FighterSheetResult(entry.FighterId, entry.IsEnemy, entry.CameraCount, failures, framing, null));
        }
        catch (Exception error)
        {
            _failed++;
            var failure = error.Message.Contains("zero textured pixels", StringComparison.Ordinal) ? FighterSheetFailure.SilhouetteEmpty : FighterSheetFailure.RendererError;
            _results.Add(new FighterSheetResult(entry.FighterId, entry.IsEnemy, entry.CameraCount, [failure], null, error.Message));
            QuietLog.Info($"FIGHTER_SHEET fighter={entry.FighterId} status=error failure={failure} reason={error.Message}");
        }
    }

    /// <summary>Places a fighter with its source opening camera, pulling back until it fits the frame.</summary>
    /// <param name="renderer">Active battle renderer.</param>
    /// <param name="captureRoot">Directory receiving the sheet.</param>
    /// <param name="fighter">Fighter manifest.</param>
    /// <returns>A task completed after an accepted framing.</returns>
    /// <exception cref="InvalidOperationException">No usable source camera or framing exists.</exception>
    private async Task<string> RenderFighterAsync(BattleRenderer renderer, string captureRoot, SheetFighter fighter)
    {
        renderer.BuildSingleModelForSheet(fighter.Path, fighter.Name, fighter.Id, fighter.Enemy);
        renderer.SetProbePose(0);

        // The sheet holds one model in slot zero even when it is placed at the enemy camp.
        var cameras = renderer.FighterCameras(enemy: false);
        if (cameras.Count == 0)
        {
            throw new InvalidOperationException("no source cameras");
        }

        var view = cameras[ViewIndex];
        if (view.Proj <= 0)
        {
            throw new InvalidOperationException($"invalid source projection={view.Proj} view={ViewIndex}");
        }

        foreach (var factor in PullBackFactors)
        {
            var framing = await TryFrameAsync(renderer, captureRoot, fighter with { CameraCount = cameras.Count }, view, factor);
            if (framing is not null)
            {
                return framing;
            }
        }

        throw new InvalidOperationException("no source camera framing contains whole fighter at x6.0");
    }

    /// <summary>Renders one framing and saves it when the fighter does not touch the frame edge.</summary>
    /// <param name="renderer">Active battle renderer.</param>
    /// <param name="captureRoot">Directory receiving the sheet.</param>
    /// <param name="fighter">Fighter manifest and camera count.</param>
    /// <param name="view">Source opening camera.</param>
    /// <param name="factor">Pull-back factor.</param>
    /// <returns>True when the framing was accepted and saved.</returns>
    /// <exception cref="InvalidOperationException">The fighter renders no textured pixel.</exception>
    private async Task<string?> TryFrameAsync(BattleRenderer renderer, string captureRoot, SheetFighter fighter, BattleModel.BattleCameraView view, float factor)
    {
        var framed = PullBack(view, factor);
        renderer.SetSourceCamera(framed, snap: true);
        renderer.ClearModelsForSheet();
        var empty = await CaptureSourceSizeAsync();
        renderer.BuildSingleModelForSheet(fighter.Path, fighter.Name, fighter.Id, fighter.Enemy);
        renderer.SetProbePose(0);

        // The sheet never enters Present, so apply the settled stage light set (fade 1) to the
        // freshly built model instead of the black fade-0 state.
        renderer.SetLightFadeForProbe(1f);
        renderer.SetSourceCamera(framed, snap: true);
        Host.AdoptSheetFighter(fighter.Id);
        var image = await CaptureSourceSizeAsync();
        var bounds = FighterSheetBounds.Measure(image, empty);
        if (bounds.TexturedPixels == 0)
        {
            throw new InvalidOperationException("zero textured pixels after source placement");
        }

        if (bounds.TouchesFrame)
        {
            return null;
        }

        Save(empty, Path.Combine(captureRoot, $"fighter-{fighter.Id}-background.png"), $"background save failed fighter={fighter.Id}");
        var output = Path.Combine(captureRoot, $"fighter-{fighter.Id}.png");
        Save(image, output, $"save failed={output}");
        var framing = factor <= 1f ? $"source-view-{ViewIndex}" : $"pulled-back x{factor.ToString("0.0", CultureInfo.InvariantCulture)}";
        QuietLog.Info($"FIGHTER_SHEET fighter={fighter.Id} status=ok enemy={fighter.Enemy} cameras={fighter.CameraCount} framing={framing} image={output}");
        return framing;
    }

    /// <summary>Waits for the next drawn frame and downsamples it to the 320x240 sheet size.</summary>
    /// <returns>The Lanczos-resized viewport image.</returns>
    private async Task<Image> CaptureSourceSizeAsync()
    {
        await Host.NextFrameDrawn();
        var image = Host.CaptureViewport();
        image.Resize(320, 240, Image.Interpolation.Lanczos);
        return image;
    }

    /// <summary>One fighter manifest rendered into the sheet.</summary>
    /// <param name="Path">Model JSON path.</param>
    /// <param name="Id">Source fighter id.</param>
    /// <param name="Enemy">Whether the manifest uses the enemy FighterInfo layout.</param>
    private sealed record SheetFighter(string Path, int Id, bool Enemy)
    {
        /// <summary>Gets the renderer node name.</summary>
        public string Name => $"fighter-{Id}";

        /// <summary>Gets the number of source cameras, once placed.</summary>
        public int CameraCount { get; init; }
    }
}
