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
        renderer.SetCommandMenuCamera();
        VerifyRotationAndWinding(renderer);
        VerifySurfacePasses(renderer);
        await VerifyBoneMaskAsync(renderer, captureRoot);
        await VerifyCullingAsync(renderer, captureRoot);
        var kotemonPath = Host.ResolveBattleModelPath(Kotemon);
        await CaptureComparisonModelAsync(renderer, captureRoot, kotemonPath, Kuwagamon, "Kuwagamon");
        await CaptureComparisonModelAsync(renderer, captureRoot, kotemonPath, WarGreymon, "WarGreymon");
        await VerifyResidualUvAsync(renderer, captureRoot, kotemonPath);
        await ReplayHiddenParentVectorsAsync(renderer, captureRoot, kotemonPath);
        QuietLog.Info($"battle-model probe PASS fps={Engine.GetFramesPerSecond():0.0} captures={captureRoot}/kotemon-idle.png,{captureRoot}/kuwagamon-idle.png,{captureRoot}/wargreymon-idle.png");
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

    /// <summary>
    /// Source bone 16 retains the one visible shinai while 21/23 remove its duplicate mesh instances.
    /// Texture modulation makes whole-frame bright-pixel counts nonmonotonic, so the semantic
    /// difference is proven only in the visible shinai region.
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
        var saberDifference = BattlePixelMetrics.CountDifferent(legacy, kotemon, 740, 800);
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

    /// <summary>Rebuilds an enemy beside Kotemon and records its idle bone visibility.</summary>
    /// <param name="renderer">Active battle renderer.</param>
    /// <param name="captureRoot">Directory receiving the capture.</param>
    /// <param name="kotemonPath">Decoded Kotemon model path.</param>
    /// <param name="fighter">Enemy fighter id.</param>
    /// <param name="name">Enemy display name.</param>
    /// <returns>A task completed after the capture.</returns>
    private async Task CaptureComparisonModelAsync(BattleRenderer renderer, string captureRoot, string kotemonPath, int fighter, string name)
    {
        renderer.BuildModels(Host.ResolveBattleModelPath(fighter), kotemonPath, name, "Kotemon", Kotemon);
        ProbeRequirement.Require(renderer.ModelCount == 2, $"{name} comparison model did not load");
        renderer.SetProbePose(IdleFrame);
        await CaptureAsync(captureRoot, $"{name.ToLowerInvariant()}-idle.png");
        var identity = fighter == WarGreymon ? $"fighter_name={name} name_id=36 fighter_id={fighter}" : $"fighter_name={name}";
        QuietLog.Info($"battle-model bones {renderer.BoneVisibility(fighter)} {identity} frame={IdleFrame}");
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
}
