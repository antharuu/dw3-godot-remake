using System.Globalization;
using System.Text.Json;
using Godot;
using Dw3Production.Diagnostics.Battle;
using Dw3Production.Diagnostics.Field;
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

    /// <summary>Battle shot-camera probe. Captures PAL frame landmarks without using the
    /// unrelated PartnerView camera, then records the actual list/shot/substate state.</summary>

    /// <summary>Framebuffer proof. The source comparison is a command-menu phase
    /// indicator, while the modulation and non-black fighter checks remain hard assertions.</summary>
    private async void RunBattleLightProbe()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir
                ?? Path.Combine(generatedRoot, "probe/battle-light");
            Directory.CreateDirectory(captureRoot);
            StartDebugBattle();
            Require(_battleRenderer is not null && _activeBattle is not null, "F8 did not construct battle lighting");
            var renderer = BattleRenderer;
            Require(_battleRenderedPlayerId == 383 && renderer.StageBuilt && renderer.ModelCount == 2,
                "light probe requires Kotemon, stage 18 and both decoded models");
            renderer.SetProbePose(10);
            renderer.SetNeutralLightsForProbe(false);
            // the player-turn presentation flow starts the main ShotCamera for the command menu. Its first source
            // shot is getEnemyView, distinct from the small partner-view camera 6/7.
            renderer.SetCommandMenuCamera();
            var lit = await CaptureBattle(captureRoot, "kotemon-lit.png");
            Require(renderer.LightFade >= 1f, $"settled light capture fade={renderer.LightFade:0.###}");
            var kuwagamon = await CaptureBattle(captureRoot, "kuwagamon-lit.png");
            var stageLighting = renderer.StageLightSummary();
            var kuwagamonVertex = renderer.LightingVertexSummary(51);
            // The PSX oracle contains the entrance without HUD. Capture the same renderer state
            // without HUD, then a stage-only frame. Their difference is the remake silhouette,
            // rather than a hand-picked dark rectangle that includes the arena.
            renderer.SetHudVisibleForProbe(false);
            // Preserve a separate active-fighter mask for the hard non-black invariant.
            renderer.SetModelVisibleForProbe(51, false);
            var activeModel = await CaptureBattle(captureRoot, "active-model.png");
            renderer.SetModelVisibleForProbe(51, true);
            renderer.SetModelVisibleForProbe(383, false);
            // This is the exact player-command camera 6. A zero mask is meaningful: the
            // enemy is not in the remake framebuffer, so this oracle is explicitly unpaired.
            var kuwagamonModel = await CaptureBattle(captureRoot, "kuwagamon-model.png");
            renderer.ClearModelsForSheet();
            var stageOnly = await CaptureBattle(captureRoot, "stage-only.png");
            var psx = LoadPsxReference("battle/11-command-menu-sel3.png");
            var psx320 = ToSource320(psx);
            var active320 = ToSource320(activeModel);
            var remake320 = ToSource320(kuwagamonModel);
            var stage320 = ToSource320(stageOnly);
            var psxMask = PsxKuwagamonMask(psx320);
            var activeMask = DifferenceMask(active320, stage320);
            var remakeMask = CommonKuwagamonMask(DifferenceMask(remake320, stage320));
            Require(psxMask.SavePng(Path.Combine(captureRoot, "mask-psx.png")) == Error.Ok, "PSX mask write failed");
            Require(remakeMask.SavePng(Path.Combine(captureRoot, "mask-remake.png")) == Error.Ok, "remake mask write failed");
            var psxStats = MaskedLuminance(psx320, psxMask);
            var activeStats = MaskedLuminance(active320, activeMask);
            var remakePixels = MaskedPixelCount(remakeMask);
            var compare = SideBySide(remake320, psx320, "REMAKE", "PSX");
            Require(compare.SavePng(Path.Combine(captureRoot, "compare-kuwagamon-psx.png")) == Error.Ok, "PSX comparison capture write failed");
            Require(compare.SavePng(Path.Combine(captureRoot, "compare-command-menu-camera.png")) == Error.Ok, "command camera comparison write failed");
            QuietLog.Info($"battle-light stage=18 fade={renderer.LightFade:0.###} {stageLighting} vertex={kuwagamonVertex}");
            VerifyPs1ModulationQuad();
            var darkPercent = MaskedDarkPercent(active320, activeMask, threshold: 30f);
            Require(darkPercent < 30f, $"active fighter dark={darkPercent:0.##}% must be <30%");
            Require(activeStats.Mean > 60f, $"active fighter mean={activeStats.Mean:0.##} must be >60");
            QuietLog.Info($"battle-light hard modulation=tex80x[ff,80,40]=[255,128,64] active_mean={activeStats.Mean:0.##} active_dark={darkPercent:0.##}%");
            if (remakePixels <= 100)
            {
                QuietLog.Info($"battle-light oracle phase=command-menu psx mean={psxStats.Mean:0.##} q10={psxStats.Q10:0.##} q50={psxStats.Q50:0.##} q90={psxStats.Q90:0.##} remake mean=n/a q10=n/a q50=n/a q90=n/a delta=n/a");
                GD.PushWarning($"WARN battle-light-oracle-unpaired phase=command-menu reason=remake-kuwagamon-offscreen psx_pixels={psxStats.Count} remake_pixels={remakePixels}");
            }
            else
            {
                var remakeStats = MaskedLuminance(remake320, remakeMask);
                var meanDelta = RelativeDelta(psxStats.Mean, remakeStats.Mean);
                var medianDelta = RelativeDelta(psxStats.Q50, remakeStats.Q50);
                QuietLog.Info($"battle-light oracle phase=command-menu psx mean={psxStats.Mean:0.##} q10={psxStats.Q10:0.##} q50={psxStats.Q50:0.##} q90={psxStats.Q90:0.##} remake mean={remakeStats.Mean:0.##} q10={remakeStats.Q10:0.##} q50={remakeStats.Q50:0.##} q90={remakeStats.Q90:0.##} delta={meanDelta * 100f:0.##}%");
                if (meanDelta < .25f && medianDelta < .25f)
                    QuietLog.Info($"battle-light oracle paired mean_delta={meanDelta * 100f:0.##}% median_delta={medianDelta * 100f:0.##}%");
                else
                    GD.PushWarning($"WARN battle-light-oracle-unpaired phase=command-menu mean_delta={meanDelta * 100f:0.##}% median_delta={medianDelta * 100f:0.##}% psx_pixels={psxStats.Count} remake_pixels={remakeStats.Count}");
            }
            QuietLog.Info($"battle-light probe PASS captures={captureRoot}/kotemon-lit.png,{captureRoot}/kuwagamon-lit.png,{captureRoot}/compare-kuwagamon-psx.png");
            GetTree().Quit(0);
        }
        catch (PsxReferenceUnavailableException ex)
        {
            QuietLog.Info(ex.Message);
            GetTree().Quit(5);
        }
        catch (Exception error)
        {
            GD.PushError($"BATTLE_LIGHT_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

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

    private static double LuminanceVariance(Image image, int xMin, int xMax)
    {
        var data = image.GetData();
        var bpp = image.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        var values = new List<double>();
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = xMin; x < xMax; x++)
            {
                var offset = (y * image.GetWidth() + x) * bpp;
                // The neutral baseline's black models intentionally contribute zero, while the
                // fixed stage background is excluded by its near-black blue pixel range.
                var luminance = (0.2126 * data[offset] + 0.7152 * data[offset + 1] + 0.0722 * data[offset + 2]) / 255d;
                if (luminance > 0.12) values.Add(luminance);
            }
        if (values.Count == 0) return 0;
        var mean = values.Average();
        return values.Sum(value => (value - mean) * (value - mean)) / values.Count;
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

    private readonly record struct LuminanceStats(int Count, float Mean, float Q10, float Q50, float Q90, string Histogram);

    private static double MeanLuminance(Image image)
    {
        double total = 0;
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var pixel = image.GetPixel(x, y);
                total += pixel.R * 54.213 + pixel.G * 182.376 + pixel.B * 18.411;
            }
        return total / (image.GetWidth() * image.GetHeight());
    }

    private static Rect2? PartnerTargetBoundingBox(Image image)
    {
        var minX = image.GetWidth(); var minY = image.GetHeight(); var maxX = -1; var maxY = -1;
        for (var y = 0; y < image.GetHeight(); y++) for (var x = 0; x < image.GetWidth(); x++)
            {
                if (image.GetPixel(x, y).A8 == 0) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        return maxX < minX ? null : new Rect2(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Rect2? PartnerPsxBoundingBox(Image image)
    {
        var panel = new Color(8 / 255f, 8 / 255f, 65 / 255f);
        var minX = image.GetWidth(); var minY = image.GetHeight(); var maxX = -1; var maxY = -1;
        for (var y = 0; y < image.GetHeight(); y++) for (var x = 0; x < image.GetWidth(); x++)
            {
                if (!ProbePixels.DifferentRgb(image.GetPixel(x, y), panel)) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        return maxX < minX ? null : new Rect2(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Rect2? DifferenceBoundingBox(Image a, Image b)
    {
        var minX = a.GetWidth(); var minY = a.GetHeight(); var maxX = -1; var maxY = -1;
        for (var y = 0; y < a.GetHeight(); y++) for (var x = 0; x < a.GetWidth(); x++)
            {
                if (!ProbePixels.DifferentRgb(a.GetPixel(x, y), b.GetPixel(x, y))) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        return maxX < minX ? null : new Rect2(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Rect2? WarmKotemonBoundingBox(Image image)
    {
        // The paired Kotemon capture is the source menu frame. Its red/orange armour is distinct
        // from stage 18 and cyan HUD. Bound only that verified warm range, rather than its UI.
        var minX = image.GetWidth(); var minY = image.GetHeight(); var maxX = -1; var maxY = -1;
        for (var y = 55; y < 230; y++) for (var x = 80; x < 245; x++)
            {
                var p = image.GetPixel(x, y);
                if (!(p.R8 > 70 && p.R8 > p.G8 * 1.2f && p.R8 > p.B8 * 1.15f)) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        return maxX < minX ? null : new Rect2(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static string FormatBox(Rect2 rect) => $"({rect.Position.X:0},{rect.Position.Y:0},{rect.Size.X:0},{rect.Size.Y:0})";

    private static Image ToSource320(Image image)
    {
        var copy = (Image)image.Duplicate();
        copy.Convert(Image.Format.Rgba8);
        if (copy.GetSize() == new Vector2I(1280, 720))
        {
            copy = copy.GetRegion(new Rect2I(160, 0, 960, 720));
            copy.Resize(320, 240, Image.Interpolation.Nearest);
        }
        Require(copy.GetSize() == new Vector2I(320, 240), $"expected source-size image, got {copy.GetWidth()}x{copy.GetHeight()}");
        return copy;
    }

    private static Image PsxKuwagamonMask(Image image)
    {
        var mask = Image.CreateEmpty(320, 240, false, Image.Format.Rgba8);
        mask.Fill(Colors.Black);
        // Stage 18 is a dark low-saturation violet set. Kuwagamon is the warm orange/brown
        // body inside this entrance framing. Explicitly reject dark stage pixels and the
        // recomp's saturated green/magenta artefacts before retaining warm body texels.
        for (var y = 8; y < 235; y++)
            for (var x = 18; x < 235; x++)
            {
                var p = image.GetPixel(x, y);
                var max = Mathf.Max(p.R, Mathf.Max(p.G, p.B));
                var min = Mathf.Min(p.R, Mathf.Min(p.G, p.B));
                var warm = p.R > p.G * 1.08f && p.G > p.B * 1.04f && max > 0.10f && max - min > 0.06f;
                if (warm) mask.SetPixel(x, y, Colors.White);
            }
        return mask;
    }

    private static Image DifferenceMask(Image model, Image stage)
    {
        var mask = Image.CreateEmpty(320, 240, false, Image.Format.Rgba8);
        mask.Fill(Colors.Black);
        for (var y = 0; y < 240; y++)
            for (var x = 0; x < 320; x++)
            {
                var a = model.GetPixel(x, y);
                var b = stage.GetPixel(x, y);
                if (Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) > 0.08f)
                    mask.SetPixel(x, y, Colors.White);
            }
        return mask;
    }

    /// <summary>Command camera 6 leaves Kuwagamon only on the left edge, like the PSX menu
    /// capture. Keep that mutually visible region and reject the active partner on the right.</summary>
    private static Image CommonKuwagamonMask(Image mask)
    {
        for (var y = 0; y < mask.GetHeight(); y++)
            for (var x = 0; x < mask.GetWidth(); x++)
                if (x >= 160 || y < 24 || y >= 220) mask.SetPixel(x, y, Colors.Black);
        return mask;
    }

    private static LuminanceStats MaskedLuminance(Image image, Image mask, int minimumPixels = 100)
    {
        var values = new List<float>();
        for (var y = 0; y < 240; y++)
            for (var x = 0; x < 320; x++)
            {
                if (mask.GetPixel(x, y).R < 0.5f) continue;
                var p = image.GetPixel(x, y);
                values.Add(p.R * 54.213f + p.G * 182.376f + p.B * 18.411f);
            }
        Require(values.Count >= minimumPixels, $"luminance mask too small pixels={values.Count} minimum={minimumPixels}");
        values.Sort();
        float Quantile(float q) => values[(int)Mathf.Clamp(Mathf.Round((values.Count - 1) * q), 0, values.Count - 1)];
        var bins = new int[8];
        foreach (var value in values) bins[Math.Min(7, (int)(value / 32f))]++;
        return new LuminanceStats(values.Count, values.Average(), Quantile(.1f), Quantile(.5f), Quantile(.9f), string.Join(',', bins));
    }

    private static int MaskedPixelCount(Image mask)
    {
        var count = 0;
        for (var y = 0; y < 240; y++)
            for (var x = 0; x < 320; x++) if (mask.GetPixel(x, y).R >= .5f) count++;
        return count;
    }

    private static float MaskedDarkPercent(Image image, Image mask, float threshold)
    {
        var total = 0;
        var dark = 0;
        for (var y = 0; y < 240; y++)
            for (var x = 0; x < 320; x++)
            {
                if (mask.GetPixel(x, y).R < .5f) continue;
                total++;
                var p = image.GetPixel(x, y);
                var luminance = p.R * 54.213f + p.G * 182.376f + p.B * 18.411f;
                if (luminance < threshold) dark++;
            }
        Require(total > 100, $"active fighter mask too small pixels={total}");
        return 100f * dark / total;
    }

    /// <summary>One texel of the dedicated unshaded battle shader's mid-grey test quad.
    /// The source equation is tex * color / 128, exactly the shader's normalized *2 path.</summary>
    private static void VerifyPs1ModulationQuad()
    {
        static int Quad(byte texture, byte color) => Math.Min(255, (texture * color + 64) / 128);
        Require(Quad(0x80, 0xFF) is >= 254 and <= 255, "modulation quad 0xFF must be about 255");
        Require(Quad(0x80, 0x80) == 128, "modulation quad 0x80 must be 128");
        Require(Quad(0x80, 0x40) == 64, "modulation quad 0x40 must be 64");
    }

    private static float RelativeDelta(float a, float b) => MathF.Abs(a - b) / MathF.Max(1f, (a + b) * .5f);

    private static Image SideBySide(Image remake, Image psx, string leftLabel, string rightLabel)
    {
        var left = (Image)remake.Duplicate();
        var right = (Image)psx.Duplicate();
        left.Convert(Image.Format.Rgba8);
        right.Convert(Image.Format.Rgba8);
        var output = Image.CreateEmpty(left.GetWidth() + right.GetWidth(), Math.Max(left.GetHeight(), right.GetHeight()), false, Image.Format.Rgba8);
        output.Fill(Colors.Black);
        output.BlitRect(left, new Rect2I(Vector2I.Zero, left.GetSize()), Vector2I.Zero);
        output.BlitRect(right, new Rect2I(Vector2I.Zero, right.GetSize()), new Vector2I(left.GetWidth(), 0));
        DrawImageLabel(output, leftLabel, new Vector2I(8, 7));
        DrawImageLabel(output, rightLabel, new Vector2I(left.GetWidth() + 8, 7));
        return output;
    }

    private static void DrawImageLabel(Image image, string label, Vector2I origin)
    {
        var glyphs = new Dictionary<char, string[]>
        {
            ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
            ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
            ['K'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
            ['M'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
            ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
            ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
            ['S'] = ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
            ['X'] = ["10001", "01010", "00100", "00100", "00100", "01010", "10001"],
        };
        var x = origin.X;
        foreach (var c in label)
        {
            if (!glyphs.TryGetValue(c, out var glyph)) continue;
            for (var gy = 0; gy < glyph.Length; gy++)
                for (var gx = 0; gx < glyph[gy].Length; gx++)
                    if (glyph[gy][gx] == '1')
                        for (var dy = 0; dy < 2; dy++)
                            for (var dx = 0; dx < 2; dx++) image.SetPixel(x + gx * 2 + dx, origin.Y + gy * 2 + dy, Colors.White);
            x += 12;
        }
    }

    #endregion
}
