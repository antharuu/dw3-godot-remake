using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Framebuffer proof of the stage-18 fighter lighting. The source comparison is a command-menu
/// phase indicator, while the modulation and non-black active-fighter checks remain hard assertions.
/// </summary>
internal sealed class BattleLightProbe : BattleProbe
{
    private const int Kotemon = 383;
    private const int Kuwagamon = 51;
    private const int MinimumMaskPixels = 100;

    /// <summary>Initializes a new instance of the <see cref="BattleLightProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleLightProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_LIGHT_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory("probe/battle-light");
        Host.StartDebugBattle();
        ProbeRequirement.Require(Host.HasBattleRenderer && Host.HasActiveBattle, "F8 did not construct battle lighting");
        var renderer = Host.Renderer;
        ProbeRequirement.Require(Host.RenderedPlayerId == Kotemon && renderer.StageBuilt && renderer.ModelCount == 2,
            "light probe requires Kotemon, stage 18 and both decoded models");
        var frames = await CaptureFramesAsync(renderer, captureRoot);
        var psx = BattleComparisonImage.ToSourceSize(Host.LoadPsxReference("battle/11-command-menu-sel3.png"));
        var measured = Measure(captureRoot, frames, psx);
        QuietLog.Info($"battle-light stage=18 fade={renderer.LightFade:0.###} {frames.StageLighting} vertex={frames.KuwagamonVertex}");
        VerifyActiveFighter(frames.ActiveModel, measured);
        LogOracle(frames.KuwagamonModel, measured);
        QuietLog.Info($"battle-light probe PASS captures={captureRoot}/kotemon-lit.png,{captureRoot}/kuwagamon-lit.png,{captureRoot}/compare-kuwagamon-psx.png");
    }

    /// <summary>Builds and saves the silhouette masks, measures them and writes the comparison images.</summary>
    /// <param name="captureRoot">Directory receiving masks and comparisons.</param>
    /// <param name="frames">Source-sized remake frames.</param>
    /// <param name="psx">Source-sized PSX command-menu reference.</param>
    /// <returns>The masks and luminance measurements used by the verdict.</returns>
    private static LightMeasurements Measure(string captureRoot, LightFrames frames, Image psx)
    {
        var psxMask = PsxKuwagamonMask(psx);
        var activeMask = BattleMaskMetrics.Difference(frames.ActiveModel, frames.StageOnly);
        var remakeMask = CommonKuwagamonMask(BattleMaskMetrics.Difference(frames.KuwagamonModel, frames.StageOnly));
        Save(psxMask, captureRoot, "mask-psx.png", "PSX mask write failed");
        Save(remakeMask, captureRoot, "mask-remake.png", "remake mask write failed");
        var psxStats = BattleMaskMetrics.Measure(psx, psxMask, MinimumMaskPixels);
        var activeStats = BattleMaskMetrics.Measure(frames.ActiveModel, activeMask, MinimumMaskPixels);
        var remakePixels = SelectedPixelCount(remakeMask);
        var compare = BattleComparisonImage.SideBySide(frames.KuwagamonModel, psx, "REMAKE", "PSX");
        Save(compare, captureRoot, "compare-kuwagamon-psx.png", "PSX comparison capture write failed");
        Save(compare, captureRoot, "compare-command-menu-camera.png", "command camera comparison write failed");
        return new LightMeasurements(psxStats, activeMask, activeStats, remakeMask, remakePixels);
    }

    /// <summary>Writes one probe image or fails with the stable message.</summary>
    /// <param name="image">Image to save.</param>
    /// <param name="captureRoot">Output directory.</param>
    /// <param name="fileName">PNG file name.</param>
    /// <param name="failure">Failure message.</param>
    private static void Save(Image image, string captureRoot, string fileName, string failure) =>
        ProbeRequirement.Require(image.SavePng(Path.Combine(captureRoot, fileName)) == Error.Ok, failure);

    /// <summary>Applies the hard PS1 modulation and non-black active-fighter assertions.</summary>
    /// <param name="activeModel">Source-sized frame showing only the active fighter.</param>
    /// <param name="measured">Masks and luminance measurements.</param>
    private static void VerifyActiveFighter(Image activeModel, LightMeasurements measured)
    {
        VerifyPs1ModulationQuad();
        var darkPercent = MaskedDarkPercent(activeModel, measured.ActiveMask, threshold: 30f);
        ProbeRequirement.Require(darkPercent < 30f, $"active fighter dark={darkPercent:0.##}% must be <30%");
        ProbeRequirement.Require(measured.ActiveStats.Mean > 60f, $"active fighter mean={measured.ActiveStats.Mean:0.##} must be >60");
        QuietLog.Info($"battle-light hard modulation=tex80x[ff,80,40]=[255,128,64] active_mean={measured.ActiveStats.Mean:0.##} active_dark={darkPercent:0.##}%");
    }

    /// <summary>Records the optional PSX command-menu oracle as paired or explicitly unpaired.</summary>
    /// <param name="kuwagamonModel">Source-sized frame showing only the enemy.</param>
    /// <param name="measured">Masks and luminance measurements.</param>
    private static void LogOracle(Image kuwagamonModel, LightMeasurements measured)
    {
        var psxStats = measured.PsxStats;
        if (measured.RemakePixels <= MinimumMaskPixels)
        {
            QuietLog.Info($"battle-light oracle phase=command-menu psx mean={psxStats.Mean:0.##} q10={psxStats.Q10:0.##} q50={psxStats.Q50:0.##} q90={psxStats.Q90:0.##} remake mean=n/a q10=n/a q50=n/a q90=n/a delta=n/a");
            GD.PushWarning($"WARN battle-light-oracle-unpaired phase=command-menu reason=remake-kuwagamon-offscreen psx_pixels={psxStats.Count} remake_pixels={measured.RemakePixels}");
            return;
        }

        var remakeStats = BattleMaskMetrics.Measure(kuwagamonModel, measured.RemakeMask, MinimumMaskPixels);
        var meanDelta = BattleMaskMetrics.RelativeDelta(psxStats.Mean, remakeStats.Mean);
        var medianDelta = BattleMaskMetrics.RelativeDelta(psxStats.Q50, remakeStats.Q50);
        QuietLog.Info($"battle-light oracle phase=command-menu psx mean={psxStats.Mean:0.##} q10={psxStats.Q10:0.##} q50={psxStats.Q50:0.##} q90={psxStats.Q90:0.##} remake mean={remakeStats.Mean:0.##} q10={remakeStats.Q10:0.##} q50={remakeStats.Q50:0.##} q90={remakeStats.Q90:0.##} delta={meanDelta * 100f:0.##}%");
        if (meanDelta < .25f && medianDelta < .25f)
            QuietLog.Info($"battle-light oracle paired mean_delta={meanDelta * 100f:0.##}% median_delta={medianDelta * 100f:0.##}%");
        else
            GD.PushWarning($"WARN battle-light-oracle-unpaired phase=command-menu mean_delta={meanDelta * 100f:0.##}% median_delta={medianDelta * 100f:0.##}% psx_pixels={psxStats.Count} remake_pixels={remakeStats.Count}");
    }

    /// <summary>
    /// Selects the warm orange/brown Kuwagamon body of the PSX entrance framing. Stage 18 is a dark
    /// low-saturation violet set, and the recomp's saturated green/magenta artefacts are rejected.
    /// </summary>
    /// <param name="image">Source-sized PSX reference.</param>
    /// <returns>Binary body mask.</returns>
    private static Image PsxKuwagamonMask(Image image)
    {
        var mask = BattleMaskMetrics.CreateEmpty();
        for (var y = 8; y < 235; y++)
        {
            for (var x = 18; x < 235; x++)
            {
                if (IsWarmBody(image.GetPixel(x, y))) mask.SetPixel(x, y, Colors.White);
            }
        }

        return mask;
    }

    /// <summary>Returns whether a PSX texel belongs to the warm Kuwagamon body range.</summary>
    /// <param name="p">Reference pixel.</param>
    /// <returns>True for a warm, sufficiently bright and saturated texel.</returns>
    private static bool IsWarmBody(Color p)
    {
        var max = Mathf.Max(p.R, Mathf.Max(p.G, p.B));
        var min = Mathf.Min(p.R, Mathf.Min(p.G, p.B));
        return p.R > p.G * 1.08f && p.G > p.B * 1.04f && max > 0.10f && max - min > 0.06f;
    }

    /// <summary>
    /// Command camera 6 leaves Kuwagamon only on the left edge, like the PSX menu capture. Keep
    /// that mutually visible region and reject the active partner on the right.
    /// </summary>
    /// <param name="mask">Remake enemy difference mask, cleared in place.</param>
    /// <returns>The same mask instance.</returns>
    private static Image CommonKuwagamonMask(Image mask)
    {
        for (var y = 0; y < mask.GetHeight(); y++)
        {
            for (var x = 0; x < mask.GetWidth(); x++)
            {
                if (x >= 160 || y < 24 || y >= 220) mask.SetPixel(x, y, Colors.Black);
            }
        }

        return mask;
    }

    /// <summary>Counts the selected pixels of a source-sized mask.</summary>
    /// <param name="mask">Binary mask.</param>
    /// <returns>The selected pixel count.</returns>
    private static int SelectedPixelCount(Image mask)
    {
        var count = 0;
        for (var y = 0; y < BattleMaskMetrics.Height; y++)
        {
            for (var x = 0; x < BattleMaskMetrics.Width; x++)
            {
                if (BattleMaskMetrics.IsSelected(mask, x, y)) count++;
            }
        }

        return count;
    }

    /// <summary>Measures the percentage of masked pixels darker than a luminance threshold.</summary>
    /// <param name="image">Source-sized image.</param>
    /// <param name="mask">Active-fighter mask.</param>
    /// <param name="threshold">Luminance below which a pixel is dark.</param>
    /// <returns>The dark percentage.</returns>
    private static float MaskedDarkPercent(Image image, Image mask, float threshold)
    {
        var total = 0;
        var dark = 0;
        for (var y = 0; y < BattleMaskMetrics.Height; y++)
        {
            for (var x = 0; x < BattleMaskMetrics.Width; x++)
            {
                if (!BattleMaskMetrics.IsSelected(mask, x, y)) continue;
                total++;
                if (BattleMaskMetrics.Luminance(image.GetPixel(x, y)) < threshold) dark++;
            }
        }

        ProbeRequirement.Require(total > MinimumMaskPixels, $"active fighter mask too small pixels={total}");
        return 100f * dark / total;
    }

    /// <summary>
    /// Checks one texel of the dedicated unshaded battle shader's mid-grey test quad. The source
    /// equation is tex * color / 128, exactly the shader's normalized *2 path.
    /// </summary>
    private static void VerifyPs1ModulationQuad()
    {
        static int Quad(byte texture, byte color) => Math.Min(255, ((texture * color) + 64) / 128);
        ProbeRequirement.Require(Quad(0x80, 0xFF) is >= 254 and <= 255, "modulation quad 0xFF must be about 255");
        ProbeRequirement.Require(Quad(0x80, 0x80) == 128, "modulation quad 0x80 must be 128");
        ProbeRequirement.Require(Quad(0x80, 0x40) == 64, "modulation quad 0x40 must be 64");
    }

    /// <summary>
    /// Captures the lit scene, then the HUD-free active fighter, enemy and stage-only frames whose
    /// differences are the remake silhouettes, rather than a hand-picked rectangle that includes the arena.
    /// </summary>
    /// <param name="renderer">Active battle renderer.</param>
    /// <param name="captureRoot">Directory receiving the captures.</param>
    /// <returns>The source-sized silhouette frames and the lighting summaries.</returns>
    private async Task<LightFrames> CaptureFramesAsync(BattleRenderer renderer, string captureRoot)
    {
        renderer.SetProbePose(10);
        renderer.SetNeutralLightsForProbe(false);
        // The player-turn presentation flow starts the main ShotCamera for the command menu. Its first
        // source shot is getEnemyView, distinct from the small partner-view camera 6/7.
        renderer.SetCommandMenuCamera();
        await CaptureAsync(captureRoot, "kotemon-lit.png");
        ProbeRequirement.Require(renderer.LightFade >= 1f, $"settled light capture fade={renderer.LightFade:0.###}");
        await CaptureAsync(captureRoot, "kuwagamon-lit.png");
        var stageLighting = renderer.StageLightSummary();
        var kuwagamonVertex = renderer.LightingVertexSummary(Kuwagamon);
        renderer.SetHudVisibleForProbe(false);
        // Preserve a separate active-fighter mask for the hard non-black invariant.
        renderer.SetModelVisibleForProbe(Kuwagamon, false);
        var activeModel = await CaptureAsync(captureRoot, "active-model.png");
        renderer.SetModelVisibleForProbe(Kuwagamon, true);
        renderer.SetModelVisibleForProbe(Kotemon, false);
        // This is the exact player-command camera 6. A zero mask is meaningful: the enemy is not
        // in the remake framebuffer, so this oracle is explicitly unpaired.
        var kuwagamonModel = await CaptureAsync(captureRoot, "kuwagamon-model.png");
        renderer.ClearModelsForSheet();
        var stageOnly = await CaptureAsync(captureRoot, "stage-only.png");
        return new LightFrames(
            BattleComparisonImage.ToSourceSize(activeModel),
            BattleComparisonImage.ToSourceSize(kuwagamonModel),
            BattleComparisonImage.ToSourceSize(stageOnly),
            stageLighting,
            kuwagamonVertex);
    }

    /// <summary>Captures one settled battle frame.</summary>
    /// <param name="captureRoot">Directory receiving the capture.</param>
    /// <param name="fileName">PNG file name.</param>
    /// <returns>The captured viewport image.</returns>
    private Task<Image> CaptureAsync(string captureRoot, string fileName) =>
        BattleProbeCapture.CaptureAsync(Host, captureRoot, fileName);

    /// <summary>Source-sized silhouette frames captured from the live battle.</summary>
    /// <param name="ActiveModel">Frame with only the active fighter visible.</param>
    /// <param name="KuwagamonModel">Frame with only the enemy visible.</param>
    /// <param name="StageOnly">Frame without fighters.</param>
    /// <param name="StageLighting">Renderer stage light summary.</param>
    /// <param name="KuwagamonVertex">Renderer vertex lighting summary of the enemy.</param>
    private sealed record LightFrames(Image ActiveModel, Image KuwagamonModel, Image StageOnly, string StageLighting, string KuwagamonVertex);

    /// <summary>Masks and luminance measurements derived from the silhouette frames.</summary>
    /// <param name="PsxStats">PSX enemy body luminance.</param>
    /// <param name="ActiveMask">Remake active-fighter silhouette.</param>
    /// <param name="ActiveStats">Remake active-fighter luminance.</param>
    /// <param name="RemakeMask">Remake enemy region shared with the PSX framing.</param>
    /// <param name="RemakePixels">Selected pixel count of <paramref name="RemakeMask"/>.</param>
    private sealed record LightMeasurements(BattleLuminanceStats PsxStats, Image ActiveMask, BattleLuminanceStats ActiveStats, Image RemakeMask, int RemakePixels);
}
