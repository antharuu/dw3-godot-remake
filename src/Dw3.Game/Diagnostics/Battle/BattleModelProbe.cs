using System.Text.Json;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Framebuffer regression of decoded fighter models: enters the real F8 battle, compares the old
/// identity-scale baseline to source bone scale and visibility, then checks back-face culling and
/// the same scale predicate on Kuwagamon, WarGreymon and the residual-UV MegaSeadramon.
/// </summary>
internal sealed class BattleModelProbe : BattleProbe
{
    private const int Kotemon = 383;
    private const int Kuwagamon = 51;
    private const int Renamon = 373;
    private const int ComparisonCameraIndex = 2;
    private const int IdleFrame = 10;

    /// <summary>DIGNAM entry 36 is WarGreymon. fighter.json resolves that name id to fighter 213.</summary>
    private const int WarGreymon = 213;

    /// <summary>MegaSeadramon exercises the residual textured primitive path on twelve source polygons.</summary>
    private const int MegaSeadramon = 61;

    /// <summary>Initializes a new instance of the <see cref="BattleModelProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleModelProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_MODEL_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory("probe/battle-model");
        Host.StartDebugBattle();
        ProbeRequirement.Require(Host.HasActiveBattle && Host.HasBattleRenderer, "F8 did not construct a battle renderer");
        var renderer = Host.Renderer;
        ProbeRequirement.Require(Host.RenderedPlayerId == Kotemon, $"F8 player fighter={Host.RenderedPlayerId}, expected Kotemon 383");
        ProbeRequirement.Require(renderer.ModelCount == 2, "F8 did not build both real models");
        if (System.Environment.GetEnvironmentVariable("DW3_BATTLE_HIDDEN_PARENT_ONLY") == "1")
        {
            await BattleHiddenParentCanvasProof.RunAsync(Host, captureRoot);
            return;
        }

        using var cameraRng = renderer.OverrideShotCameraRngForProbe(new BattleProofCameraRng());
        renderer.SetCommandMenuCamera();
        await VerifyCanvasLayerVisibilityAsync(Host, renderer, captureRoot);
        await VerifyCanvasCalibrationAsync(Host, renderer, captureRoot);
        SaveRawPartnerView(renderer, captureRoot);
        VerifyRotationAndWinding(renderer);
        VerifySurfacePasses(renderer);
        await BattleRasterPresenceProof.RunAsync(Host, captureRoot);
        await VerifyBoneMaskAsync(renderer, captureRoot);
        await VerifyCullingAsync(renderer, captureRoot);
        var kotemonPath = Host.ResolveBattleModelPath(Kotemon);
        var comparisonCamera = renderer.FighterCameras(enemy: false)[ComparisonCameraIndex];
        var renamon = await CaptureComparisonModelAsync(renderer, captureRoot, kotemonPath, Renamon, "Renamon", comparisonCamera);
        var kuwagamon = await CaptureComparisonModelAsync(renderer, captureRoot, kotemonPath, Kuwagamon, "Kuwagamon", comparisonCamera);
        var warGreymon = await CaptureComparisonModelAsync(renderer, captureRoot, kotemonPath, WarGreymon, "WarGreymon", comparisonCamera);
        VerifySilhouetteComparisons(renamon, kuwagamon, warGreymon, comparisonCamera);
        await VerifyResidualUvAsync(renderer, captureRoot, kotemonPath);
        await ReplayHiddenParentVectorsAsync(renderer, captureRoot, kotemonPath);
        if (System.Environment.GetEnvironmentVariable("DW3_BATTLE_STP_PROOF") == "1")
        {
            await BattleRasterOverlapProof.RunAsync(Host, captureRoot);
        }

        if (System.Environment.GetEnvironmentVariable("DW3_BATTLE_CADENCE_PROOF") == "1")
        {
            await BattleRasterCadenceProof.RunAsync(Host, captureRoot);
        }

        QuietLog.Info($"battle-model probe PASS fps={Engine.GetFramesPerSecond():0.0} captures={captureRoot}/kotemon-idle.png,{captureRoot}/kuwagamon-idle.png,{captureRoot}/wargreymon-idle.png");
    }

    /// <summary>Captures the production Canvas fixture and proves UV, RGB and alpha coverage in framebuffer samples.</summary>
    /// <param name="host">Live host that captures the same canvas parent used by fighters.</param>
    /// <param name="renderer">Renderer exposing the production Polygon2D command layer.</param>
    /// <param name="captureRoot">Directory receiving RED/GREEN calibration evidence.</param>
    /// <returns>A task completed after every required sample has been classified.</returns>
    private static async Task VerifyCanvasCalibrationAsync(IBattleProbeHost host, BattleRenderer renderer, string captureRoot)
    {
        var baseline = await BattleProbeCapture.CaptureAsync(host, captureRoot, "canvas-calibration-baseline.png");
        var legacy = System.Environment.GetEnvironmentVariable("DW3_BATTLE_CANVAS_CALIBRATION_LEGACY") == "1";
        renderer.SetModelLayerCanvasCalibrationVisibleForProbe(true, legacy);
        var calibration = await BattleProbeCapture.CaptureAsync(host, captureRoot, legacy ? "canvas-calibration-red.png" : "canvas-calibration-green.png");
        renderer.SetModelLayerCanvasCalibrationVisibleForProbe(false, legacy);
        VerifyCanvasRgbSamples(calibration);
        VerifyCanvasAlphaSamples(baseline, calibration);
        QuietLog.Info($"battle-canvas-calibration mode={(legacy ? "RED" : "GREEN")} uv=once nearest=2x2 rgb128=64 rgb255=128 alpha=0,128,255");
    }

    /// <summary>Checks the two vertex modulation levels against all nearest-filtered fixture quadrants.</summary>
    /// <param name="image">Framebuffer containing the opaque RGB quadrants.</param>
    private static void VerifyCanvasRgbSamples(Image image)
    {
        var expected = new[]
        {
            new CanvasSample(24, 24, new Color8(64, 0, 0), "rgb128-red"),
            new CanvasSample(40, 24, new Color8(0, 64, 0), "rgb128-green"),
            new CanvasSample(24, 40, new Color8(0, 0, 64), "rgb128-blue"),
            new CanvasSample(40, 40, new Color8(64, 64, 64), "rgb128-white"),
            new CanvasSample(72, 24, new Color8(128, 0, 0), "rgb255-red"),
            new CanvasSample(88, 24, new Color8(0, 128, 0), "rgb255-green"),
            new CanvasSample(72, 40, new Color8(0, 0, 128), "rgb255-blue"),
            new CanvasSample(88, 40, new Color8(128, 128, 128), "rgb255-white"),
        };
        foreach (var sample in expected)
        {
            RequireRgb(image.GetPixel(sample.X, sample.Y), sample.Rgb, sample.Name);
        }
    }

    /// <summary>Classifies raw transparent, STP and non-STP texels through the paired production shader passes.</summary>
    /// <param name="baseline">Framebuffer before the synthetic alpha fixture was drawn.</param>
    /// <param name="calibration">Framebuffer after the synthetic alpha fixture was drawn.</param>
    private static void VerifyCanvasAlphaSamples(Image baseline, Image calibration)
    {
        var transparent = calibration.GetPixel(24, 72);
        var stp = calibration.GetPixel(40, 72);
        var nonStp = calibration.GetPixel(56, 72);
        ProbeRequirement.Require(transparent == baseline.GetPixel(24, 72), "canvas alpha=0 must discard through both coverage passes");
        ProbeRequirement.Require(stp != baseline.GetPixel(40, 72) && stp.R8 is > 96 and < 255, "canvas alpha=128 must select STP half blend");
        RequireRgb(nonStp, new Color8(255, 255, 255), "canvas alpha=255 non-STP opaque");
    }

    /// <summary>Requires one opaque RGB framebuffer sample within the texture upload rounding tolerance.</summary>
    /// <param name="actual">Framebuffer pixel read from the production capture.</param>
    /// <param name="expected">Expected RGB after the PS1 x2 vertex modulation.</param>
    /// <param name="name">Diagnostic fixture sample name.</param>
    private static void RequireRgb(Color actual, Color8 expected, string name)
    {
        var matches = Math.Abs(actual.R8 - expected.R) <= 2
            && Math.Abs(actual.G8 - expected.G) <= 2
            && Math.Abs(actual.B8 - expected.B) <= 2;
        ProbeRequirement.Require(matches, $"canvas {name} actual=({actual.R8},{actual.G8},{actual.B8}) expected=({expected.R},{expected.G},{expected.B})");
    }

    /// <summary>Writes and validates the raw 100x60 PartnerView target beside the composed model capture.</summary>
    /// <param name="renderer">Renderer owning the live partner target.</param>
    /// <param name="captureRoot">Directory receiving the raw PartnerView PNG.</param>
    private static void SaveRawPartnerView(BattleRenderer renderer, string captureRoot)
    {
        var path = Path.Combine(captureRoot, "kotemon-partner-view-raw.png");
        ProbeRequirement.Require(renderer.SavePartnerView(path) == Error.Ok, "could not save raw Kotemon PartnerView target");
        var target = Image.LoadFromFile(path);
        ProbeRequirement.Require(target.GetWidth() == 100 && target.GetHeight() == 60, "PartnerView target must remain 100x60");
        ProbeRequirement.Require(renderer.PartnerViewOpaquePixels() > 300, "raw PartnerView lacks recognizable textured geometry");
        QuietLog.Info($"battle-model partner-view raw={path} size={target.GetWidth()}x{target.GetHeight()} opaque={renderer.PartnerViewOpaquePixels()}");
    }

    /// <summary>Proves that the actual production canvas parent reaches the offscreen battle framebuffer.</summary>
    /// <param name="host">Live host that captures the production framebuffer.</param>
    /// <param name="renderer">Active renderer carrying the production model layer.</param>
    /// <param name="captureRoot">Directory receiving the control capture.</param>
    /// <returns>A task completed after the control capture is checked and removed.</returns>
    private static async Task VerifyCanvasLayerVisibilityAsync(IBattleProbeHost host, BattleRenderer renderer, string captureRoot)
    {
        renderer.SetModelLayerDiagnosticTriangleVisibleForProbe(true);
        var image = await BattleProbeCapture.CaptureAsync(host, captureRoot, "canvas-layer-control.png");
        renderer.SetModelLayerDiagnosticTriangleVisibleForProbe(false);
        var redPixels = CountRedPixels(image);
        ProbeRequirement.Require(redPixels > 5000, $"model canvas control triangle is not visible red_pixels={redPixels}");
        QuietLog.Info($"battle-model canvas-control parent=external-canvas red_pixels={redPixels}");
    }

    /// <summary>Counts the deliberately saturated control colour without classifying stage or HUD pixels.</summary>
    /// <param name="image">Framebuffer containing the diagnostic control.</param>
    /// <returns>Number of control-red pixels.</returns>
    private static int CountRedPixels(Image image)
    {
        var count = 0;
        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 200; x++)
            {
                var pixel = image.GetPixel(x, y);
                count += pixel.R8 > 240 && pixel.G8 < 16 && pixel.B8 < 16 ? 1 : 0;
            }
        }

        return count;
    }

    /// <summary>Checks the source rotation order and that both fighters keep source triangle winding.</summary>
    /// <param name="renderer">Active battle renderer.</param>
    private static void VerifyRotationAndWinding(BattleRenderer renderer)
    {
        var rotationDiff = renderer.RotationOracleMaxDiff();
        ProbeRequirement.Require(rotationDiff < 1e-4f, $"battle rotation oracle maxdiff={rotationDiff:R}");
        QuietLog.Info($"battle-rotation-oracle maxdiff={rotationDiff:R} order=Rz*Ry*Rx*S");
        renderer.SetProbePose(IdleFrame);
        foreach (var fighter in new[] { Kotemon, Kuwagamon })
        {
            var winding = renderer.WindingAgreement(fighter);
            ProbeRequirement.Require(winding.Total > 0, $"battle winding fighter={fighter} has no triangles");
            var percent = winding.Agree * 100d / winding.Total;
            ProbeRequirement.Require(percent >= 90d, $"battle winding fighter={fighter} agree={percent:0.00} below 90");
            QuietLog.Info($"battle-winding fighter={fighter} agree={percent:0.00} total={winding.Total} agree_count={winding.Agree}");
        }
    }

    /// <summary>
    /// Fighters with no ABR primitive must stay in the opaque (depth-writing) pass. Only the stage's
    /// ABR polygons may be translucent.
    /// </summary>
    /// <param name="renderer">Active battle renderer.</param>
    private static void VerifySurfacePasses(BattleRenderer renderer)
    {
        var kotemonPasses = renderer.SurfacePasses(Kotemon);
        var stagePasses = renderer.SurfacePasses(-1);
        ProbeRequirement.Require(
            kotemonPasses.Opaque > 0 && kotemonPasses.Translucent == 0,
            $"Kotemon 383 surfaces must all be opaque opaque={kotemonPasses.Opaque} translucent={kotemonPasses.Translucent}");
        QuietLog.Info($"battle-model passes fighter=383 opaque={kotemonPasses.Opaque} translucent={kotemonPasses.Translucent} stage-opaque={stagePasses.Opaque} stage-translucent={stagePasses.Translucent}");
    }

    /// <summary>Counts bright pixels of the left (player) half of a frame.</summary>
    /// <param name="image">Captured frame.</param>
    /// <returns>The bright-pixel count.</returns>
    private static int LeftHalfBright(Image image) => BattlePixelMetrics.CountBright(image, 0, image.GetWidth() / 2);

    /// <summary>Checks all three comparison silhouettes under the unchanged source camera.</summary>
    /// <param name="renamon">Small-model logical silhouette.</param>
    /// <param name="kuwagamon">Large-model logical silhouette.</param>
    /// <param name="warGreymon">Asymmetric-model logical silhouette.</param>
    /// <param name="camera">Fixed source camera captured before any comparison model is rebuilt.</param>
    private static void VerifySilhouetteComparisons(BattleSilhouetteMetrics renamon, BattleSilhouetteMetrics kuwagamon, BattleSilhouetteMetrics warGreymon, BattleModel.BattleCameraView camera)
    {
        ProbeRequirement.Require(
            renamon.HeightFraction > 0f && kuwagamon.HeightFraction > 0f && warGreymon.WidthFraction > 0f,
            "comparison silhouettes lost logical-raster area");
        QuietLog.Info($"battle-projection comparison camera_index={ComparisonCameraIndex} pos=({camera.CamPos[0]},{camera.CamPos[1]},{camera.CamPos[2]}) ref=({camera.CamRef[0]},{camera.CamRef[1]},{camera.CamRef[2]}) proj={camera.Proj} small={BattleComparisonImage.FormatBounds(renamon.Bounds)} large={BattleComparisonImage.FormatBounds(kuwagamon.Bounds)} asymmetric={BattleComparisonImage.FormatBounds(warGreymon.Bounds)} logical=320x240 margins=160x0 scale=3");
    }

    /// <summary>
    /// Source bone 16 retains the one visible shinai while 21/23 remove its duplicate mesh instances.
    /// Texture modulation makes whole-frame bright-pixel counts nonmonotonic, so the semantic
    /// duplicate-bone difference is measured over the frame. A separate bone-16 removal proves
    /// actual shinai contribution in main and raw PartnerView without an arbitrary strip.
    /// </summary>
    /// <param name="renderer">Active battle renderer.</param>
    /// <param name="captureRoot">Directory receiving the captures.</param>
    /// <returns>A task completed after the bone-mask assertion.</returns>
    private async Task VerifyBoneMaskAsync(BattleRenderer renderer, string captureRoot)
    {
        renderer.ShowAllBonesForProbe();
        var legacy = await CaptureAsync(captureRoot, "kotemon-legacy-all-bones.png");
        var legacyPixels = LeftHalfBright(legacy);
        renderer.SetProbePose(IdleFrame);
        ProbeRequirement.Require(
            renderer.BoneVisible(Kotemon, 16) && !renderer.BoneVisible(Kotemon, 21) && !renderer.BoneVisible(Kotemon, 23),
            "Kotemon idle frame 10 must show saber bone 16 only");
        var kotemon = await CaptureAsync(captureRoot, "kotemon-idle.png");
        var kotemonPixels = LeftHalfBright(kotemon);
        var saberDifference = BattlePixelMetrics.CountDifferent(legacy, kotemon, 0, kotemon.GetWidth());
        ProbeRequirement.Require(
            kotemonPixels > 1000 && saberDifference > 0,
            $"Kotemon source mask must retain textured body and change saber region pixels={saberDifference} before={legacyPixels} after={kotemonPixels}");
        QuietLog.Info($"battle-model bones {renderer.BoneVisibility(Kotemon)} visible_sabre=16 frame={IdleFrame} saber_region_diff={saberDifference} pixels-before={legacyPixels} pixels-after={kotemonPixels}");
    }

    /// <summary>Checks that back-face culling keeps the visible Kotemon body.</summary>
    /// <param name="renderer">Active battle renderer.</param>
    /// <param name="captureRoot">Directory receiving the captures.</param>
    /// <returns>A task completed after the culling assertion.</returns>
    private async Task VerifyCullingAsync(BattleRenderer renderer, string captureRoot)
    {
        renderer.SetCullModeForProbe(BaseMaterial3D.CullModeEnum.Disabled);
        var noCullPixels = LeftHalfBright(await CaptureAsync(captureRoot, "kotemon-nocull.png"));
        renderer.SetCullModeForProbe(BaseMaterial3D.CullModeEnum.Back);
        var culledPixels = LeftHalfBright(await CaptureAsync(captureRoot, "kotemon-cull-back.png"));
        ProbeRequirement.Require(
            culledPixels > 1000 && culledPixels * 4 > noCullPixels,
            $"back culling removed the visible Kotemon body no-cull={noCullPixels} back={culledPixels}");
        QuietLog.Info($"battle-model culling fighter=383 mode=Back textured-no-cull={noCullPixels} textured-back={culledPixels} orientation=correct");
    }

    /// <summary>Rebuilds an enemy beside Kotemon and measures its isolated silhouette in the original raster.</summary>
    /// <param name="renderer">Active battle renderer.</param>
    /// <param name="captureRoot">Directory receiving the paired captures.</param>
    /// <param name="kotemonPath">Decoded Kotemon model path.</param>
    /// <param name="fighter">Enemy fighter id.</param>
    /// <param name="name">Enemy display name.</param>
    /// <param name="comparisonCamera">Fixed source camera captured before rebuilding the compared fighter.</param>
    /// <returns>Logical-raster silhouette metrics isolated from the unchanged scene.</returns>
    private async Task<BattleSilhouetteMetrics> CaptureComparisonModelAsync(BattleRenderer renderer, string captureRoot, string kotemonPath, int fighter, string name, BattleModel.BattleCameraView comparisonCamera)
    {
        renderer.BuildModels(Host.ResolveBattleModelPath(fighter), kotemonPath, name, "Kotemon", Kotemon);
        ProbeRequirement.Require(renderer.ModelCount == 2, $"{name} comparison model did not load");
        renderer.StopShotCamera();
        renderer.SetSourceCamera(comparisonCamera, snap: true);
        renderer.SetProbePose(IdleFrame);
        renderer.SetModelVisibleForProbe(fighter, false);
        var withoutFighter = await BattleProbeCapture.CaptureRawAsync(Host, captureRoot, $"{name.ToLowerInvariant()}-without.png");
        renderer.SetModelVisibleForProbe(fighter, true);
        var withFighter = await BattleProbeCapture.CaptureRawAsync(Host, captureRoot, $"{name.ToLowerInvariant()}-idle.png");
        var sourceBounds = BattlePixelMetrics.DifferenceBoundsAboveThreshold(
            BattleComparisonImage.ToSourceSize(withFighter),
            BattleComparisonImage.ToSourceSize(withoutFighter),
            threshold: 30);
        ProbeRequirement.Require(sourceBounds is not null, $"{name} source silhouette is empty");
        var metrics = BattleSilhouetteMetrics.FromBounds(sourceBounds.GetValueOrDefault());
        var identity = fighter == WarGreymon ? $"fighter_name={name} name_id=36 fighter_id={fighter}" : $"fighter_name={name}";
        QuietLog.Info($"battle-model bones {renderer.BoneVisibility(fighter)} {identity} frame={IdleFrame} silhouette={BattleComparisonImage.FormatBounds(metrics.Bounds)} anchor=({metrics.Anchor.X:0},{metrics.Anchor.Y:0}) proportions=({metrics.WidthFraction:0.###},{metrics.HeightFraction:0.###})");
        return metrics;
    }

    /// <summary>
    /// The decoded mesh contract does not clear POLY_FT/GT UV/tpage state when 0x9 disables new UV
    /// bytes. MegaSeadramon must still render its residual textured primitives.
    /// </summary>
    /// <param name="renderer">Active battle renderer.</param>
    /// <param name="captureRoot">Directory receiving the capture.</param>
    /// <param name="kotemonPath">Decoded Kotemon model path.</param>
    /// <returns>A task completed after the residual-UV assertion.</returns>
    private async Task VerifyResidualUvAsync(BattleRenderer renderer, string captureRoot, string kotemonPath)
    {
        renderer.BuildModels(kotemonPath, Host.ResolveBattleModelPath(MegaSeadramon), "Kotemon", "MegaSeadramon", MegaSeadramon);
        ProbeRequirement.Require(renderer.ModelCount == 2, "MegaSeadramon residual-UV model did not load");
        ProbeRequirement.Require(renderer.UntexturedPolygonCount(MegaSeadramon) > 0, "MegaSeadramon has no decoded residual-UV polygons");
        renderer.SetProbePose(0);
        var mega = await CaptureAsync(captureRoot, "megaseadramon-untextured.png");
        var megaPixels = BattlePixelMetrics.CountBright(mega, mega.GetWidth() / 2, mega.GetWidth());
        ProbeRequirement.Require(megaPixels > 0, "MegaSeadramon residual-UV model rendered zero textured pixels");
        QuietLog.Info($"battle-model untextured polygons={renderer.UntexturedPolygonCount(MegaSeadramon)} fighter={MegaSeadramon} textured-pixels={megaPixels}");
    }

    /// <summary>Replays externally supplied real parent-hidden vectors through both production render paths.</summary>
    /// <param name="renderer">Live production renderer.</param>
    /// <param name="captureRoot">Directory receiving the affected-fighter frame pair.</param>
    /// <param name="kotemonPath">Decoded Kotemon model path used as the opposing fighter.</param>
    /// <returns>A task completed when every requested vector has been checked.</returns>
    private async Task ReplayHiddenParentVectorsAsync(BattleRenderer renderer, string captureRoot, string kotemonPath)
    {
        var path = System.Environment.GetEnvironmentVariable("DW3_BATTLE_SKELETON_VECTORS");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var vectors = document.RootElement.GetProperty("parent_hidden_child_visible_cases")
            .EnumerateArray().Select(item => new HiddenParentVector(
                item.GetProperty("fighter").GetInt32(),
                item.GetProperty("frame").GetInt32(),
                item.GetProperty("parent").GetInt32(),
                item.GetProperty("child").GetInt32())).ToArray();
        ProbeRequirement.Require(vectors.Length > 0, "hidden-parent vector replay received no cases");
        var replayed = 0;
        foreach (var group in vectors.GroupBy(vector => vector.Fighter))
        {
            var fighter = group.Key;
            renderer.BuildModels(Host.ResolveBattleModelPath(fighter), kotemonPath, $"fighter-{fighter}", "Kotemon", Kotemon);
            foreach (var vector in group)
            {
                renderer.SetProbePose(vector.Frame);
                ProbeRequirement.Require(!renderer.BoneVisible(fighter, vector.Parent), $"vector parent remained visible fighter={fighter} frame={vector.Frame} bone={vector.Parent}");
                ProbeRequirement.Require(renderer.BoneVisible(fighter, vector.Child), $"vector child hidden fighter={fighter} frame={vector.Frame} bone={vector.Child}");
                ProbeRequirement.Require(renderer.LegacySurfaceVisible(fighter, vector.Child), $"legacy child not visible in tree fighter={fighter} frame={vector.Frame} bone={vector.Child}");
                ProbeRequirement.Require(renderer.DrawablePolygonCount(fighter, vector.Child) > 0, $"painter child has no drawable primitive fighter={fighter} frame={vector.Frame} bone={vector.Child}");
                replayed++;
            }
        }

        renderer.BuildModels(Host.ResolveBattleModelPath(11), kotemonPath, "fighter-11", "Kotomen", Kotemon);
        renderer.SetProbePose(0);
        var before = await CaptureAsync(captureRoot, "fighter-11-before-hidden-parent.png");
        renderer.SetProbePose(vectors.First(vector => vector.Fighter == 11).Frame);
        var after = await CaptureAsync(captureRoot, "fighter-11-child-visible-under-hidden-parent.png");
        var distinct = BattlePixelMetrics.CountDifferent(before, after, 0, before.GetWidth());
        ProbeRequirement.Require(distinct > 0, "fighter 11 affected-pose framebuffer made no change");
        QuietLog.Info($"battle-hidden-parent replayed={replayed} fighters={vectors.Select(vector => vector.Fighter).Distinct().Count()} fighter11-frame-diff={distinct}");
    }

    /// <summary>Captures one settled battle frame.</summary>
    /// <param name="captureRoot">Directory receiving the capture.</param>
    /// <param name="fileName">PNG file name.</param>
    /// <returns>The captured viewport image.</returns>
    private Task<Image> CaptureAsync(string captureRoot, string fileName) =>
        BattleProbeCapture.CaptureAsync(Host, captureRoot, fileName);

    /// <summary>One externally supplied parent-hidden/child-visible production replay vector.</summary>
    /// <param name="Fighter">Affected fighter identifier.</param>
    /// <param name="Frame">Source pose frame.</param>
    /// <param name="Parent">Locally hidden parent bone.</param>
    /// <param name="Child">Locally visible child bone.</param>
    private readonly record struct HiddenParentVector(int Fighter, int Frame, int Parent, int Child);

    /// <summary>One named calibration fixture coordinate and expected framebuffer RGB.</summary>
    /// <param name="X">Framebuffer x coordinate.</param>
    /// <param name="Y">Framebuffer y coordinate.</param>
    /// <param name="Rgb">Expected RGB after shader modulation.</param>
    /// <param name="Name">Stable log name.</param>
    private readonly record struct CanvasSample(int X, int Y, Color8 Rgb, string Name);

    /// <summary>One compact 8-bit RGB triplet used for framebuffer assertions.</summary>
    /// <param name="R">Expected red channel.</param>
    /// <param name="G">Expected green channel.</param>
    /// <param name="B">Expected blue channel.</param>
    private readonly record struct Color8(byte R, byte G, byte B);
}
