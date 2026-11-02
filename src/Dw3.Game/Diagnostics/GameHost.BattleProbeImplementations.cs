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

    /// <summary>Framebuffer regression: enters the real F8 battle, compares the old identity-scale baseline to source bone scale/visibility, then checks back-face culling and the same scale predicate on Kuwagamon and WarGreymon.</summary>
    private async void RunBattleModelProbe()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir
                ?? Path.Combine(generatedRoot, "probe/battle-model");
            Directory.CreateDirectory(captureRoot);

            StartDebugBattle();
            Require(_activeBattle is not null && _battleRenderer is not null, "F8 did not construct a battle renderer");
            var renderer = _battleRenderer ?? throw new InvalidOperationException("battle renderer unavailable after F8");
            Require(_battleRenderedPlayerId == 383, $"F8 player fighter={_battleRenderedPlayerId}, expected Kotemon 383");
            Require(renderer.ModelCount == 2, "F8 did not build both real models");
            renderer.SetCommandMenuCamera();
            var rotationDiff = renderer.RotationOracleMaxDiff();
            Require(rotationDiff < 1e-4f, $"battle rotation oracle maxdiff={rotationDiff:R}");
            QuietLog.Info($"battle-rotation-oracle maxdiff={rotationDiff:R} order=Rz*Ry*Rx*S");
            renderer.SetProbePose(10);
            foreach (var fighter in new[] { 383, 51 })
            {
                var winding = renderer.WindingAgreement(fighter);
                Require(winding.Total > 0, $"battle winding fighter={fighter} has no triangles");
                var percent = winding.Agree * 100d / winding.Total;
                Require(percent >= 90d, $"battle winding fighter={fighter} agree={percent:0.00} below 90");
                QuietLog.Info($"battle-winding fighter={fighter} agree={percent:0.00} total={winding.Total} agree_count={winding.Agree}");
            }

            // Fighters with no ABR primitive must stay in the opaque
            // (depth-writing) pass; only the stage's ABR polygons may be translucent.
            var kotemonPasses = renderer.SurfacePasses(383);
            var stagePasses = renderer.SurfacePasses(-1);
            Require(kotemonPasses.Opaque > 0 && kotemonPasses.Translucent == 0,
                $"Kotemon 383 surfaces must all be opaque opaque={kotemonPasses.Opaque} translucent={kotemonPasses.Translucent}");
            QuietLog.Info($"battle-model passes fighter=383 opaque={kotemonPasses.Opaque} translucent={kotemonPasses.Translucent} stage-opaque={stagePasses.Opaque} stage-translucent={stagePasses.Translucent}");

            const int idleFrame = 10;
            renderer.ShowAllBonesForProbe();
            var legacy = await CaptureBattle(captureRoot, "kotemon-legacy-all-bones.png");
            var legacyPixels = CountBrightPixels(legacy, 0, legacy.GetWidth() / 2);

            renderer.SetProbePose(idleFrame);
            Require(renderer.BoneVisible(383, 16) && !renderer.BoneVisible(383, 21) && !renderer.BoneVisible(383, 23),
                "Kotemon idle frame 10 must show saber bone 16 only");
            var kotemon = await CaptureBattle(captureRoot, "kotemon-idle.png");
            var kotemonPixels = CountBrightPixels(kotemon, 0, kotemon.GetWidth() / 2);
            // Source bone 16 retains the one visible shinai while 21/23 remove its duplicate
            // mesh instances. Texture modulation makes whole-frame bright-pixel counts nonmonotonic,
            // so prove the semantic difference only in the visible shinai region.
            var saberDifference = CountDifferentPixels(legacy, kotemon, 790, 930);
            Require(kotemonPixels > 1000 && saberDifference > 0,
                $"Kotemon source mask must retain textured body and change saber region pixels={saberDifference} before={legacyPixels} after={kotemonPixels}");
            QuietLog.Info($"battle-model bones {renderer.BoneVisibility(383)} visible_sabre=16 frame={idleFrame} saber_region_diff={saberDifference} pixels-before={legacyPixels} pixels-after={kotemonPixels}");

            renderer.SetCullModeForProbe(BaseMaterial3D.CullModeEnum.Disabled);
            var noCull = await CaptureBattle(captureRoot, "kotemon-nocull.png");
            var noCullPixels = CountBrightPixels(noCull, 0, noCull.GetWidth() / 2);
            renderer.SetCullModeForProbe(BaseMaterial3D.CullModeEnum.Back);
            var culled = await CaptureBattle(captureRoot, "kotemon-cull-back.png");
            var culledPixels = CountBrightPixels(culled, 0, culled.GetWidth() / 2);
            Require(culledPixels > 1000 && culledPixels * 4 > noCullPixels,
                $"back culling removed the visible Kotemon body no-cull={noCullPixels} back={culledPixels}");
            QuietLog.Info($"battle-model culling fighter=383 mode=Back textured-no-cull={noCullPixels} textured-back={culledPixels} orientation=correct");

            var kotemonPath = ResolveBattleModelPath(383);
            var kuwagamonPath = ResolveBattleModelPath(51);
            renderer.BuildModels(kuwagamonPath, kotemonPath, "Kuwagamon", "Kotemon", 383);
            Require(renderer.ModelCount == 2, "Kuwagamon comparison model did not load");
            renderer.SetProbePose(idleFrame);
            await CaptureBattle(captureRoot, "kuwagamon-idle.png");
            QuietLog.Info($"battle-model bones {renderer.BoneVisibility(51)} fighter_name=Kuwagamon frame={idleFrame}");

            // DIGNAM entry 36 is WarGreymon. fighter.json resolves that name id to fighter 213.
            const int warGreymon = 213;
            var warGreymonPath = ResolveBattleModelPath(warGreymon);
            renderer.BuildModels(warGreymonPath, kotemonPath, "WarGreymon", "Kotemon", 383);
            Require(renderer.ModelCount == 2, "WarGreymon comparison model did not load");
            renderer.SetProbePose(idleFrame);
            await CaptureBattle(captureRoot, "wargreymon-idle.png");
            QuietLog.Info($"battle-model bones {renderer.BoneVisibility(warGreymon)} fighter_name=WarGreymon name_id=36 fighter_id={warGreymon} frame={idleFrame}");

            // the decoded mesh contract:766-795 does not clear POLY_FT/GT UV/tpage state when
            // 0x9 disables new UV bytes. MegaSeadramon 61 exercises that residual
            // textured primitive path on twelve source polygons.
            const int megaSeadramon = 61;
            var megaSeadramonPath = ResolveBattleModelPath(megaSeadramon);
            renderer.BuildModels(kotemonPath, megaSeadramonPath, "Kotemon", "MegaSeadramon", megaSeadramon);
            Require(renderer.ModelCount == 2, "MegaSeadramon residual-UV model did not load");
            Require(renderer.UntexturedPolygonCount(megaSeadramon) > 0, "MegaSeadramon has no decoded residual-UV polygons");
            renderer.SetProbePose(0);
            var mega = await CaptureBattle(captureRoot, "megaseadramon-untextured.png");
            var megaPixels = CountBrightPixels(mega, mega.GetWidth() / 2, mega.GetWidth());
            Require(megaPixels > 0, "MegaSeadramon residual-UV model rendered zero textured pixels");
            QuietLog.Info($"battle-model untextured polygons={renderer.UntexturedPolygonCount(megaSeadramon)} fighter={megaSeadramon} textured-pixels={megaPixels}");

            QuietLog.Info($"battle-model probe PASS fps={Engine.GetFramesPerSecond():0.0} captures={captureRoot}/kotemon-idle.png,{captureRoot}/kuwagamon-idle.png,{captureRoot}/wargreymon-idle.png");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"BATTLE_MODEL_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

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

    private static int CountBrightPixels(Image image, int xMin, int xMax)
    {
        var data = image.GetData();
        var bytesPerPixel = image.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        var count = 0;
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = xMin; x < xMax; x++)
            {
                var offset = (y * image.GetWidth() + x) * bytesPerPixel;
                if (data[offset] + data[offset + 1] + data[offset + 2] > 120) count++;
            }
        return count;
    }

    private static int CountDifferentPixels(Image a, Image b, int xMin, int xMax)
    {
        var ad = a.GetData(); var bd = b.GetData();
        var bytesPerPixel = a.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        var count = 0;
        for (var y = 0; y < a.GetHeight(); y++)
            for (var x = xMin; x < xMax; x++)
            {
                var offset = (y * a.GetWidth() + x) * bytesPerPixel;
                if (Math.Abs(ad[offset] - bd[offset]) + Math.Abs(ad[offset + 1] - bd[offset + 1]) + Math.Abs(ad[offset + 2] - bd[offset + 2]) > 30) count++;
            }
        return count;
    }

    private async System.Threading.Tasks.Task<Image> CaptureBattle(string captureRoot, string fileName)
    {
        // The normal _Process path presents every display frame. Test drivers intentionally do
        // not enter it, so a single CaptureBattle used to leave the source 60-PAL-frame stage
        // fade at 1/60 and wrote black fighter silhouettes. Advance the real Present path to its
        // steady command state before accepting any visual probe capture.
        while (BattleRenderer.LightFade < 1f)
        {
            BattleRenderer.Present(BuildBattleView());
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        BattleRenderer.Present(BuildBattleView());
        // Visibility toggles used for silhouette masks are queued by the SubViewport; consume a
        // second rendered frame so the framebuffer is not the previous scene.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var path = Path.Combine(captureRoot, fileName);
        Require(image.SavePng(path) == Error.Ok, $"battle capture failed={path}");
        return image;
    }

    #endregion
}
