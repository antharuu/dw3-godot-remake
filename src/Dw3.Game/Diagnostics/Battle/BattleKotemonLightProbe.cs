using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Compares the command-menu Kotemon lighting distribution against the private PSX oracle.</summary>
internal sealed class BattleKotemonLightProbe : BattleProbe
{
    private static readonly string[] Regions = ["helmet", "body", "fur"];

    /// <summary>Initializes a new instance of the <see cref="BattleKotemonLightProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleKotemonLightProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_KOTEMON_LIGHT_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory("probe/battle-kotemon-light");
        Host.StartDebugBattle();
        ProbeRequirement.Require(Host.HasActiveBattle && Host.RenderedPlayerId == 383,
            "Kotemon light oracle requires F8 fighter 383");
        var renderer = Host.Renderer;
        renderer.SetProbePose(10);
        renderer.SetNeutralLightsForProbe(false);
        renderer.SetCommandMenuCamera();
        renderer.SetHudVisibleForProbe(false);
        var remake = BattleComparisonImage.ToSourceSize(await BattleProbeCapture.CaptureAsync(Host, captureRoot, "kotemon-command-menu.png"));
        renderer.SetModelVisibleForProbe(383, false);
        var withoutKotemon = BattleComparisonImage.ToSourceSize(await BattleProbeCapture.CaptureAsync(Host, captureRoot, "command-menu-without-kotemon.png"));
        var silhouette = BattleMaskMetrics.Difference(remake, withoutKotemon);
        var source = BattleComparisonImage.ToSourceSize(Host.LoadPsxReference("battle/kotemon-383-command-menu-0.png"));
        var psxMasks = CreatePsxMasks(source);
        var remakeMasks = CreateRemakeMasks(remake, silhouette);
        await SaveMasksAndLogRegionsAsync(captureRoot, source, remake, psxMasks, remakeMasks);
        VerifyGlobalLuminance(captureRoot, source, remake, psxMasks.Values, remakeMasks.Values, renderer);
    }

    /// <summary>Saves the global and regional masks then writes the stable oracle telemetry.</summary>
    /// <param name="captureRoot">Directory receiving probe images.</param>
    /// <param name="source">Normalized PSX reference image.</param>
    /// <param name="remake">Normalized remake image.</param>
    /// <param name="psxMasks">PSX masks keyed by named region.</param>
    /// <param name="remakeMasks">Remake masks keyed by named region.</param>
    /// <returns>A completed task after all regional outputs are saved.</returns>
    private static Task SaveMasksAndLogRegionsAsync(
        string captureRoot,
        Image source,
        Image remake,
        IReadOnlyDictionary<string, Image> psxMasks,
        IReadOnlyDictionary<string, Image> remakeMasks)
    {
        var psxGlobal = UnionMasks(psxMasks.Values);
        var remakeGlobal = UnionMasks(remakeMasks.Values);
        ProbeRequirement.Require(psxGlobal.SavePng(Path.Combine(captureRoot, "mask-psx.png")) == Error.Ok, "PSX global mask save failed");
        ProbeRequirement.Require(remakeGlobal.SavePng(Path.Combine(captureRoot, "mask-remake.png")) == Error.Ok, "remake global mask save failed");
        foreach (var region in Regions)
        {
            SaveAndLogRegion(captureRoot, source, remake, region, psxMasks[region], remakeMasks[region]);
        }

        return Task.CompletedTask;
    }

    /// <summary>Saves one mask pair and records its paired luminance measurements.</summary>
    /// <param name="captureRoot">Directory receiving probe images.</param>
    /// <param name="source">Normalized PSX reference image.</param>
    /// <param name="remake">Normalized remake image.</param>
    /// <param name="region">Stable named region.</param>
    /// <param name="psxMask">PSX selection mask.</param>
    /// <param name="remakeMask">Remake selection mask.</param>
    private static void SaveAndLogRegion(string captureRoot, Image source, Image remake, string region, Image psxMask, Image remakeMask)
    {
        ProbeRequirement.Require(psxMask.SavePng(Path.Combine(captureRoot, $"mask-psx-{region}.png")) == Error.Ok, $"PSX {region} mask save failed");
        ProbeRequirement.Require(remakeMask.SavePng(Path.Combine(captureRoot, $"mask-remake-{region}.png")) == Error.Ok, $"remake {region} mask save failed");
        var psxStats = BattleMaskMetrics.Measure(source, psxMask, 8);
        var remakeStats = BattleMaskMetrics.Measure(remake, remakeMask, 8);
        QuietLog.Info($"battle-light oracle fighter=383 phase=command-menu region={region} psx pixels={psxStats.Count} mean={psxStats.Mean:0.##} q10={psxStats.Q10:0.##} q50={psxStats.Q50:0.##} q90={psxStats.Q90:0.##} hist={psxStats.Histogram} remake pixels={remakeStats.Count} mean={remakeStats.Mean:0.##} q10={remakeStats.Q10:0.##} q50={remakeStats.Q50:0.##} q90={remakeStats.Q90:0.##} hist={remakeStats.Histogram} delta={BattleMaskMetrics.RelativeDelta(psxStats.Mean, remakeStats.Mean) * 100f:0.##}%");
    }

    /// <summary>Checks and records the aggregate model-luminance contract.</summary>
    /// <param name="captureRoot">Directory receiving the comparison image.</param>
    /// <param name="source">Normalized PSX reference image.</param>
    /// <param name="remake">Normalized remake image.</param>
    /// <param name="psxMasks">PSX masks to union.</param>
    /// <param name="remakeMasks">Remake masks to union.</param>
    /// <param name="renderer">Renderer used to report source vertex lighting.</param>
    private static void VerifyGlobalLuminance(string captureRoot, Image source, Image remake, IEnumerable<Image> psxMasks, IEnumerable<Image> remakeMasks, BattleRenderer renderer)
    {
        var psxGlobal = UnionMasks(psxMasks);
        var remakeGlobal = UnionMasks(remakeMasks);
        var psxStats = BattleMaskMetrics.Measure(source, psxGlobal, 24);
        var remakeStats = BattleMaskMetrics.Measure(remake, remakeGlobal, 24);
        var delta = BattleMaskMetrics.RelativeDelta(psxStats.Mean, remakeStats.Mean);
        var compare = BattleComparisonImage.SideBySide(remake, source, "REMAKE", "PSX");
        ProbeRequirement.Require(compare.SavePng(Path.Combine(captureRoot, "compare-kotemon-luminance.png")) == Error.Ok, "Kotemon comparison save failed");
        QuietLog.Info($"battle-light oracle fighter=383 phase=command-menu region=global psx pixels={psxStats.Count} mean={psxStats.Mean:0.##} q10={psxStats.Q10:0.##} q50={psxStats.Q50:0.##} q90={psxStats.Q90:0.##} hist={psxStats.Histogram} remake pixels={remakeStats.Count} mean={remakeStats.Mean:0.##} q10={remakeStats.Q10:0.##} q50={remakeStats.Q50:0.##} q90={remakeStats.Q90:0.##} hist={remakeStats.Histogram} delta={delta * 100f:0.##}% vertex={renderer.LightingVertexSummary(383)}");
        ProbeRequirement.Require(delta < .25f, $"Kotemon global luminance delta={delta * 100f:0.##}% must be <25%");
        QuietLog.Info($"battle-light probe PASS captures={captureRoot}/compare-kotemon-luminance.png masks=published");
    }

    /// <summary>Builds the three source-colour selection masks.</summary>
    /// <param name="image">Normalized PSX reference image.</param>
    /// <returns>Masks keyed by stable region name.</returns>
    private static Dictionary<string, Image> CreatePsxMasks(Image image) => new()
    {
        ["helmet"] = RegionMask(image, new Rect2I(263, 151, 37, 38), p => p.B > (p.R * 1.15f) && p.B > (p.G * 1.08f) && p.B > .18f),
        ["body"] = RegionMask(image, new Rect2I(250, 164, 40, 44), p => p.R > (p.G * 1.22f) && p.R > (p.B * 1.12f) && p.R > .22f),
        ["fur"] = RegionMask(image, new Rect2I(244, 180, 32, 37), p => p.R > .42f && p.G > .42f && p.B > .42f && Range(p) < .38f),
    };

    /// <summary>Builds remake masks relative to the measured fighter silhouette.</summary>
    /// <param name="image">Normalized remake image.</param>
    /// <param name="silhouette">Remake fighter silhouette mask.</param>
    /// <returns>Masks keyed by stable region name.</returns>
    private static Dictionary<string, Image> CreateRemakeMasks(Image image, Image silhouette)
    {
        var box = MaskBounds(silhouette) ?? throw new InvalidOperationException("Kotemon remake silhouette absent");
        return new()
        {
            ["helmet"] = RegionMask(image, silhouette, Relative(box, .42f, .00f, .42f, .38f), p => p.B > (p.R * 1.12f) && p.B > (p.G * 1.04f) && p.B > .15f),
            ["body"] = RegionMask(image, silhouette, Relative(box, .28f, .25f, .55f, .48f), p => p.R > (p.G * 1.18f) && p.R > (p.B * 1.08f) && p.R > .18f),
            ["fur"] = RegionMask(image, silhouette, Relative(box, .00f, .42f, .58f, .58f), p => p.R > .38f && p.G > .38f && p.B > .38f && Range(p) < .42f),
        };
    }

    /// <summary>Returns the channel range of a colour.</summary>
    /// <param name="color">Colour to measure.</param>
    /// <returns>Largest channel minus smallest channel.</returns>
    private static float Range(Color color) => Mathf.Max(color.R, Mathf.Max(color.G, color.B)) - Mathf.Min(color.R, Mathf.Min(color.G, color.B));

    /// <summary>Computes a silhouette-relative rectangular region.</summary>
    /// <param name="box">Silhouette bounds.</param>
    /// <param name="x">Relative left.</param>
    /// <param name="y">Relative top.</param>
    /// <param name="width">Relative width.</param>
    /// <param name="height">Relative height.</param>
    /// <returns>Integer source-pixel rectangle.</returns>
    private static Rect2I Relative(Rect2I box, float x, float y, float width, float height) => new(
        Mathf.FloorToInt(box.Position.X + box.Size.X * x),
        Mathf.FloorToInt(box.Position.Y + box.Size.Y * y),
        Mathf.Max(1, Mathf.CeilToInt(box.Size.X * width)),
        Mathf.Max(1, Mathf.CeilToInt(box.Size.Y * height)));

    /// <summary>Builds a mask from one image region and predicate.</summary>
    /// <param name="image">Image to sample.</param>
    /// <param name="region">Clipped candidate region.</param>
    /// <param name="select">Colour selection predicate.</param>
    /// <returns>Binary white-on-black selection mask.</returns>
    private static Image RegionMask(Image image, Rect2I region, Func<Color, bool> select) => RegionMask(image, null, region, select);

    /// <summary>Builds a mask constrained by an optional silhouette.</summary>
    /// <param name="image">Image to sample.</param>
    /// <param name="silhouette">Optional white-on-black silhouette constraint.</param>
    /// <param name="region">Clipped candidate region.</param>
    /// <param name="select">Colour selection predicate.</param>
    /// <returns>Binary white-on-black selection mask.</returns>
    private static Image RegionMask(Image image, Image? silhouette, Rect2I region, Func<Color, bool> select)
    {
        var mask = BattleMaskMetrics.CreateEmpty();
        for (var y = Math.Max(0, region.Position.Y); y < Math.Min(240, region.End.Y); y++)
        {
            for (var x = Math.Max(0, region.Position.X); x < Math.Min(320, region.End.X); x++)
            {
                if ((silhouette is null || silhouette.GetPixel(x, y).R >= .5f) && select(image.GetPixel(x, y))) mask.SetPixel(x, y, Colors.White);
            }
        }

        return mask;
    }

    /// <summary>Unions a set of binary source-sized masks.</summary>
    /// <param name="masks">Masks to merge.</param>
    /// <returns>A white pixel wherever any input mask is white.</returns>
    private static Image UnionMasks(IEnumerable<Image> masks)
    {
        var output = BattleMaskMetrics.CreateEmpty();
        foreach (var mask in masks)
        {
            CopyMaskPixels(mask, output);
        }

        return output;
    }

    /// <summary>Copies white pixels from a source mask to a target union mask.</summary>
    /// <param name="source">Mask to read.</param>
    /// <param name="target">Mask to update.</param>
    private static void CopyMaskPixels(Image source, Image target)
    {
        for (var y = 0; y < 240; y++)
        {
            for (var x = 0; x < 320; x++)
            {
                if (source.GetPixel(x, y).R >= .5f) target.SetPixel(x, y, Colors.White);
            }
        }
    }

    /// <summary>Finds the tight bounds of white pixels in a binary mask.</summary>
    /// <param name="mask">Mask to inspect.</param>
    /// <returns>Bounds, or null when the mask contains no selected pixel.</returns>
    private static Rect2I? MaskBounds(Image mask)
    {
        var minimum = new Vector2I(320, 240);
        var maximum = new Vector2I(-1, -1);
        for (var y = 0; y < 240; y++)
        {
            for (var x = 0; x < 320; x++)
            {
                if (mask.GetPixel(x, y).R < .5f) continue;
                minimum = new Vector2I(Math.Min(minimum.X, x), Math.Min(minimum.Y, y));
                maximum = new Vector2I(Math.Max(maximum.X, x), Math.Max(maximum.Y, y));
            }
        }

        return maximum.X < 0 ? null : new Rect2I(minimum, maximum - minimum + Vector2I.One);
    }
}
