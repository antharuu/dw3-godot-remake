using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Negative-capable framebuffer checks on the production source-quad and fighter command path.</summary>
internal static class BattleRasterPresenceProof
{
    /// <summary>Requires complete source-strip coverage and a measured fighter and saber contribution in each view.</summary>
    /// <param name="host">Active offscreen production host.</param>
    /// <param name="root">Private evidence directory.</param>
    /// <returns>A task completed after the real framebuffer checks.</returns>
    public static async Task RunAsync(IBattleProbeHost host, string root)
    {
        await VerifyQuadAsync(host, root);
        await BattleRasterBlendFixtureProof.RunAsync(host, root);
        var renderer = host.Renderer;
        renderer.SetProbePose(10);
        renderer.SetProbeAnimationFrame(10);
        var main = new BattleRasterControl();
        var partner = new BattleRasterControl();
        renderer.RasterForProbe.ProbeControl = main;
        renderer.PartnerForProbe.RasterForProbe.ProbeControl = partner;
        await VerifyContributionAsync(host, root, main, partner);
        renderer.RasterForProbe.ProbeControl = null;
        renderer.PartnerForProbe.RasterForProbe.ProbeControl = null;
    }

    /// <summary>Exercises a sixteen-pixel source strip through the real Draw method, not a boundary-order fixture.</summary>
    /// <param name="host">Host that presents and captures the production Canvas.</param>
    /// <param name="root">Directory receiving the paired coverage controls.</param>
    /// <returns>A task completed after the 256-pixel assertion and its removed-indices control.</returns>
    private static async Task VerifyQuadAsync(IBattleProbeHost host, string root)
    {
        using var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(new Color(1, 0, 1));
        using var texture = ImageTexture.CreateFromImage(image);
        var polygon = new BattleModel.SourcePolygon(0, 0, 4, 0, -1, [], [Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero], [Colors.White, Colors.White, Colors.White, Colors.White]);
        var primitive = new BattleRasterPrimitive(0, polygon, [new(16, 16), new(32, 16), new(16, 32), new(32, 32)], texture, -2);
        var control = new BattleRasterControl { Fixture = [primitive] };
        host.Renderer.RasterForProbe.ProbeControl = control;
        control.ClearIndices = System.Environment.GetEnvironmentVariable("DW3_BATTLE_PROOF_CLEAR_INDICES") == "1";
        using var green = await BattleProbeCapture.CaptureAsync(host, root, "source-quad.png");
        ProbeRequirement.Require(CountMagenta(green) == 256, $"source strip coverage={CountMagenta(green)} expected=256");
        control.ClearIndices = true;
        using var red = await BattleProbeCapture.CaptureAsync(host, root, "source-quad-no-indices.png");
        ProbeRequirement.Require(CountMagenta(red) == 0, "source strip negative control did not remove coverage");
        host.Renderer.RasterForProbe.ProbeControl = null;
        QuietLog.Info("battle-source-quad coverage=256/256 clear-indices=0/256 actual-executor=Polygon2D");
    }

    /// <summary>Checks the exact sixteen-by-sixteen synthetic destination without counting stage or HUD colours.</summary>
    /// <param name="image">Production framebuffer.</param>
    /// <returns>Number of fully covered magenta pixels.</returns>
    private static int CountMagenta(Image image)
    {
        var count = 0;
        for (var y = 16; y < 32; y++)
        {
            for (var x = 16; x < 32; x++)
            {
                var pixel = image.GetPixel(x, y);
                count += pixel.R8 == 255 && pixel.G8 == 0 && pixel.B8 == 255 ? 1 : 0;
            }
        }

        return count;
    }

    /// <summary>Measures fighter-only and bone-16-only masks while retaining stage, HUD and the opposing model.</summary>
    /// <param name="host">Live production host with pose and HUD clock pinned.</param>
    /// <param name="root">Directory receiving main and raw partner controls.</param>
    /// <param name="main">Interventions on the main collector output.</param>
    /// <param name="partner">Interventions on the raw partner collector output.</param>
    /// <returns>A task completed after the hidden-target negative control and saber masks.</returns>
    private static async Task VerifyContributionAsync(IBattleProbeHost host, string root, BattleRasterControl main, BattleRasterControl partner)
    {
        main.HiddenFighter = partner.HiddenFighter = 383;
        using var hidden = await BattleProbeCapture.CaptureAsync(host, root, "kotemon-hidden-main.png");
        host.Renderer.SavePartnerView(Path.Combine(root, "kotemon-hidden-raw.png"));
        using var rawHidden = Image.LoadFromFile(Path.Combine(root, "kotemon-hidden-raw.png"));
        var hideTarget = System.Environment.GetEnvironmentVariable("DW3_BATTLE_PROOF_HIDE_TARGET") == "1";
        main.HiddenFighter = partner.HiddenFighter = hideTarget ? 383 : -1;
        using var shown = await BattleProbeCapture.CaptureAsync(host, root, "kotemon-shown-main.png");
        host.Renderer.SavePartnerView(Path.Combine(root, "kotemon-shown-raw.png"));
        using var rawShown = Image.LoadFromFile(Path.Combine(root, "kotemon-shown-raw.png"));
        var mainCount = Different(hidden, shown);
        var rawCount = Different(rawHidden, rawShown);
        ProbeRequirement.Require(mainCount > 300 && rawCount > 300, $"fighter contribution main={mainCount} raw={rawCount}, expected >300 in both views");
        main.HiddenBone = partner.HiddenBone = 16;
        using var noSaber = await BattleProbeCapture.CaptureAsync(host, root, "kotemon-no-saber-main.png");
        host.Renderer.SavePartnerView(Path.Combine(root, "kotemon-no-saber-raw.png"));
        using var rawNoSaber = Image.LoadFromFile(Path.Combine(root, "kotemon-no-saber-raw.png"));
        var saberMain = Different(shown, noSaber);
        var saberRaw = Different(rawShown, rawNoSaber);
        ProbeRequirement.Require(saberMain > 10 && saberRaw > 10, $"bone16 contribution main={saberMain} raw={saberRaw}");
        QuietLog.Info($"battle-fighter-contribution fighter=383 frame=10 main={mainCount} raw={rawCount} saber-bone=16 main={saberMain} raw={saberRaw} hidden-target-oracle=RED");
    }

    /// <summary>Counts changed framebuffer pixels, with a small tolerance for upload rounding only.</summary>
    /// <param name="first">Hidden-target control.</param>
    /// <param name="second">Visible-target framebuffer.</param>
    /// <returns>Changed pixels, not a brightness count.</returns>
    private static int Different(Image first, Image second)
    {
        var count = 0;
        for (var y = 0; y < first.GetHeight(); y++)
        {
            for (var x = 0; x < first.GetWidth(); x++)
            {
                if (first.GetWidth() > 100 && IsPartnerPixel(x, y))
                {
                    continue;
                }

                var a = first.GetPixel(x, y);
                var b = second.GetPixel(x, y);
                count += Math.Abs(a.R8 - b.R8) + Math.Abs(a.G8 - b.G8) + Math.Abs(a.B8 - b.B8) + Math.Abs(a.A8 - b.A8) > 6 ? 1 : 0;
            }
        }

        return count;
    }

    /// <summary>Excludes the separately tested PartnerView from the main-model contribution count.</summary>
    /// <param name="x">Main framebuffer column.</param>
    /// <param name="y">Main framebuffer row.</param>
    /// <returns>Whether the pixel belongs to the source 100x60 thumbnail rectangle at 208,76.</returns>
    private static bool IsPartnerPixel(int x, int y)
    {
        var left = (208 * BattleHudLayout.Scale) + BattleHudLayout.OffsetX;
        var top = 76 * BattleHudLayout.Scale;
        return x >= left && x < left + (100 * BattleHudLayout.Scale)
            && y >= top && y < top + (60 * BattleHudLayout.Scale);
    }
}
