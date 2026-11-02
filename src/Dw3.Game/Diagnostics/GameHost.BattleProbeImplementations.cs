using System.Globalization;
using System.Text.Json;
using Godot;
using Dw3Production.Diagnostics.Battle;
using Dw3Production.Logging;
using Dw3Production.Presentation;

namespace Dw3Production;

public partial class GameHost
{
    /// <summary>Loads a private reference image for optional battle comparisons.</summary>
    /// <param name="subPath">Forward-slash path relative to <c>DW3_PSX_REFERENCE_DIR</c>.</param>
    /// <returns>The decoded reference image.</returns>
    /// <exception cref="PsxReferenceUnavailableException">The root, file, or decoded image is unavailable.</exception>
    private static Image LoadPsxReference(string subPath)
    {
        var referenceDirectory = System.Environment.GetEnvironmentVariable("DW3_PSX_REFERENCE_DIR");
        if (referenceDirectory is null)
        {
            throw new PsxReferenceUnavailableException(
                "set DW3_PSX_REFERENCE_DIR to the root directory that contains battle/... frames");
        }

        var fullPath = Path.Combine(referenceDirectory, subPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            throw new PsxReferenceUnavailableException($"file not found: {fullPath}");
        }

        var image = Image.LoadFromFile(fullPath);
        if (image is null || image.IsEmpty())
        {
            throw new PsxReferenceUnavailableException($"image unreadable: {fullPath}");
        }

        return image;
    }

    #region Battle diagnostic probes

    /// <summary>Render every decoded fighter through the production BattleRenderer.
    /// This is an offline/Xvfb diagnostic path, not an interactive feature. One FIGHTER_SHEET
    /// line is emitted for every source manifest and failures are kept as explicit records.</summary>
    private async void RunFighterSheet()
    {
        var generatedRoot = _options.ReferenceDir ?? _dataRoot;
        var captureRoot = _options.CaptureDir
            ?? Path.Combine(generatedRoot, "probe/fighter-sheet");
        Directory.CreateDirectory(captureRoot);
        var modelsRoot = Path.Combine(_dataRoot, "data", "battle", "models");
        var paths = Directory.EnumerateFiles(modelsRoot, "fighter-*-model.json")
            .OrderBy(ParseSheetFighterId).ToArray();
        var failed = 0;
        try
        {
            StartDebugBattle();
            var renderer = _battleRenderer ?? throw new InvalidOperationException("F8 did not construct BattleRenderer");
            if (!renderer.StageBuilt) renderer.BuildStage(ResolveBattleStagePath(18), 18);
            renderer.Show();
            renderer.SetLightFadeForProbe(1f);
            renderer.ClearModelsForSheet();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var background = GetViewport().GetTexture().GetImage();
            background.Resize(320, 240, Image.Interpolation.Lanczos);
            Require(background.SavePng(Path.Combine(captureRoot, "stage18-background.png")) == Error.Ok, "sheet stage background save failed");
            foreach (var path in paths)
            {
                var fighter = ParseSheetFighterId(path);
                try
                {
                    var source = ReadSheetSource(path);
                    renderer.BuildSingleModelForSheet(path, $"fighter-{fighter}", fighter, source.Enemy);
                    renderer.SetProbePose(0);
                    // The sheet holds one model in slot zero even when it is placed at the enemy camp.
                    var cameras = renderer.FighterCameras(enemy: false);
                    if (cameras.Count == 0) throw new InvalidOperationException("no source cameras");
                    // View zero is the source opening shot for both FighterInfo layouts. The old
                    // sheet used view 6, a partner turn close-up and an enemy-facing reverse shot.
                    const int viewIndex = 0;
                    var view = cameras[viewIndex];
                    if (view.Proj <= 0) throw new InvalidOperationException($"invalid source projection={view.Proj} view={viewIndex}");
                    var accepted = false;
                    foreach (var factor in new[] { 1f, 1.3f, 1.7f, 2.2f, 3f, 4f, 6f })
                    {
                        var framed = PullBackSheetCamera(view, factor);
                        renderer.SetSourceCamera(framed, snap: true);
                        renderer.ClearModelsForSheet();
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        var empty = GetViewport().GetTexture().GetImage();
                        empty.Resize(320, 240, Image.Interpolation.Lanczos);
                        renderer.BuildSingleModelForSheet(path, $"fighter-{fighter}", fighter, source.Enemy);
                        renderer.SetProbePose(0);
                        // The sheet never enters Present, so apply the settled stage light set
                        // (fade 1) to the freshly built model instead of the black fade-0 state.
                        renderer.SetLightFadeForProbe(1f);
                        renderer.SetSourceCamera(framed, snap: true);
                        _battlePlayerCameraActive = false;
                        _battleRenderedPlayerId = fighter;
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        var image = GetViewport().GetTexture().GetImage();
                        image.Resize(320, 240, Image.Interpolation.Lanczos);
                        var bounds = SheetBounds(image, empty);
                        if (bounds.TexturedPixels == 0) throw new InvalidOperationException("zero textured pixels after source placement");
                        if (bounds.TouchesFrame) continue;
                        Require(empty.SavePng(Path.Combine(captureRoot, $"fighter-{fighter}-background.png")) == Error.Ok,
                            $"background save failed fighter={fighter}");
                        var output = Path.Combine(captureRoot, $"fighter-{fighter}.png");
                        Require(image.SavePng(output) == Error.Ok, $"save failed={output}");
                        var framing = factor == 1f ? $"source-view-{viewIndex}" : $"pulled-back x{factor.ToString("0.0", CultureInfo.InvariantCulture)}";
                        QuietLog.Info($"FIGHTER_SHEET fighter={fighter} status=ok enemy={source.Enemy} cameras={cameras.Count} framing={framing} image={output}");
                        accepted = true;
                        break;
                    }
                    if (!accepted) throw new InvalidOperationException("no source camera framing contains whole fighter at x6.0");
                }
                catch (Exception error)
                {
                    failed++;
                    QuietLog.Info($"FIGHTER_SHEET fighter={fighter} status=error reason={error.Message}");
                }
            }
            QuietLog.Info($"FIGHTER_SHEET complete total={paths.Length} failed={failed} root={captureRoot}");
            GetTree().Quit(failed == 0 ? 0 : 3);
        }
        catch (Exception error)
        {
            GD.PushError($"FIGHTER_SHEET_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private static int ParseSheetFighterId(string path)
    {
        const string prefix = "fighter-";
        const string suffix = "-model.json";
        var name = Path.GetFileName(path);
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)) return int.MaxValue;
        return int.TryParse(name[prefix.Length..^suffix.Length], out var fighter) ? fighter : int.MaxValue;
    }

    private readonly record struct SheetSource(bool Enemy);

    private static SheetSource ReadSheetSource(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var cameras = document.RootElement.GetProperty("cameras");
        // FighterInfoEnemy has three views, while FighterInfo has twelve (battle_model.py).
        return new SheetSource(cameras.GetArrayLength() == 3);
    }

    private static BattleModel.BattleCameraView PullBackSheetCamera(BattleModel.BattleCameraView view, float factor)
    {
        if (factor == 1f) return view;
        var pos = view.CamPos; var reference = view.CamRef;
        return new BattleModel.BattleCameraView(
            [reference[0] + Mathf.RoundToInt((pos[0] - reference[0]) * factor),
             reference[1] + Mathf.RoundToInt((pos[1] - reference[1]) * factor),
             reference[2] + Mathf.RoundToInt((pos[2] - reference[2]) * factor)], reference, view.Proj);
    }

    private static (int TexturedPixels, bool TouchesFrame) SheetBounds(Image image, Image background)
    {
        if (image.GetWidth() != background.GetWidth() || image.GetHeight() != background.GetHeight())
            throw new InvalidOperationException("sheet capture/background dimensions differ");
        var minX = image.GetWidth(); var minY = image.GetHeight(); var maxX = -1; var maxY = -1; var count = 0;
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var a = image.GetPixel(x, y); var b = background.GetPixel(x, y);
                if (Mathf.Abs(a.R - b.R) * 255f + Mathf.Abs(a.G - b.G) * 255f + Mathf.Abs(a.B - b.B) * 255f <= 45f) continue;
                count++; minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        return (count, count > 0 && (minX == 0 || minY == 0 || maxX == image.GetWidth() - 1 || maxY == image.GetHeight() - 1));
    }

    #endregion
}
