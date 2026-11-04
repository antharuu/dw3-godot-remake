using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Calibrates STP-only blending, non-STP coverage and transparent discard in both production targets.</summary>
internal static class BattleRasterBlendFixtureProof
{
    /// <summary>Runs independent eight-bit equations over fabricated inputs through the actual GPU executor.</summary>
    /// <param name="host">Production offscreen host.</param>
    /// <param name="root">Private evidence directory.</param>
    /// <returns>A task completed after both modes and their wrong-blend controls.</returns>
    public static async Task RunAsync(IBattleProbeHost host, string root)
    {
        using var foregroundImage = Image.CreateEmpty(4, 1, false, Image.Format.Rgba8);
        foregroundImage.SetPixel(0, 0, Colors.Transparent);
        foregroundImage.SetPixel(1, 0, new Color(200f / 255, 90f / 255, 30f / 255, 128f / 255));
        foregroundImage.SetPixel(2, 0, new Color(200f / 255, 90f / 255, 30f / 255));
        foregroundImage.SetPixel(3, 0, new Color(0, 0, 0, 128f / 255));
        using var backgroundImage = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        backgroundImage.Fill(new Color(100f / 255, 80f / 255, 40f / 255));
        using var foreground = ImageTexture.CreateFromImage(foregroundImage);
        using var background = ImageTexture.CreateFromImage(backgroundImage);
        var control = new BattleRasterControl();
        host.Renderer.RasterForProbe.ProbeControl = control;
        host.Renderer.PartnerForProbe.RasterForProbe.ProbeControl = control;
        foreach (var mode in new[] { 0, 1 })
        {
            control.Fixture = [Primitive(background, -1), Primitive(foreground, mode)];
            control.Limit = 1;
            using var destination = await CaptureAsync(host, root, $"blend-fixture-{mode}-destination");
            control.Limit = 2;
            using var actual = await CaptureAsync(host, root, $"blend-fixture-{mode}-actual");
            control.WrongBlend = true;
            using var wrong = await CaptureAsync(host, root, $"blend-fixture-{mode}-wrong");
            control.WrongBlend = false;
            Verify(destination, actual, wrong, mode);
            using var rawDestination = Image.LoadFromFile(Path.Combine(root, $"blend-fixture-{mode}-destination-raw.png"));
            using var rawActual = Image.LoadFromFile(Path.Combine(root, $"blend-fixture-{mode}-actual-raw.png"));
            using var rawWrong = Image.LoadFromFile(Path.Combine(root, $"blend-fixture-{mode}-wrong-raw.png"));
            Verify(rawDestination, rawActual, rawWrong, mode);
        }

        host.Renderer.RasterForProbe.ProbeControl = null;
        host.Renderer.PartnerForProbe.RasterForProbe.ProbeControl = null;
        QuietLog.Info("battle-blend-fixture PASS views=main,raw modes=0,1 alpha=0,128,255 black-STP=retained clamp-before-half=255 additive-saturation=255 tolerance=2 domain=8bit-not-PSX5bit");
    }

    /// <summary>Builds a source-strip quad for the same executor that receives collected fighters.</summary>
    /// <param name="texture">Fabricated texture page including raw alpha classification.</param>
    /// <param name="abr">Runtime ABR mode, or minus one for the opaque destination.</param>
    /// <returns>An unsplit source quad in the raw target's coordinate range.</returns>
    private static BattleRasterPrimitive Primitive(Texture2D texture, int abr)
    {
        var value = abr < 0 ? new Color(128f / 255, 128f / 255, 128f / 255) : Colors.White;
        var polygon = new BattleModel.SourcePolygon(0, 0, 4, 0, abr, [], [Vector2.Zero, Vector2.Right, Vector2.Down, Vector2.One], [value, value, value, value]);
        return new BattleRasterPrimitive(0, polygon, [new(8, 16), new(72, 16), new(8, 32), new(72, 32)], texture, -2);
    }

    /// <summary>Checks independent equations with explicit source saturation before fixed-function blending.</summary>
    /// <param name="destination">Observed background, from only the opaque prefix.</param>
    /// <param name="actual">Observed framebuffer with the source command added.</param>
    /// <param name="wrong">Observed framebuffer with only the source command forced opaque.</param>
    /// <param name="mode">Runtime ABR mode zero or one.</param>
    private static void Verify(Image destination, Image actual, Image wrong, int mode)
    {
        var foreground = new[] { new Vector3(0, 0, 0), new Vector3(255, 180, 60), new Vector3(255, 180, 60), Vector3.Zero };
        for (var i = 0; i < 4; i++)
        {
            var x = 16 + (i * 16);
            var dest = Rgb(destination.GetPixel(x, 24));
            var expected = i switch
            {
                0 => dest,
                2 => foreground[i],
                _ => mode == 0 ? (dest + foreground[i]) / 2 : (dest + foreground[i]).Clamp(Vector3.Zero, new Vector3(255, 255, 255)),
            };
            var pixel = Rgb(actual.GetPixel(x, 24));
            ProbeRequirement.Require(MaxError(pixel, expected) <= 2, $"blend fixture mode={mode} class={i} actual={pixel} expected={expected}");
            if (i == 1)
            {
                ProbeRequirement.Require(MaxError(Rgb(wrong.GetPixel(x, 24)), expected) > 2, "wrong blend did not fail the fixture oracle");
            }
        }
    }

    /// <summary>Calculates the largest absolute byte-channel error.</summary>
    /// <param name="actual">Observed framebuffer bytes.</param>
    /// <param name="expected">Independent equation result before integer rounding.</param>
    /// <returns>The maximum error in eight-bit channel units.</returns>
    private static float MaxError(Vector3 actual, Vector3 expected)
    {
        var error = (actual - expected).Abs();
        return Math.Max(error.X, Math.Max(error.Y, error.Z));
    }

    /// <summary>Extracts unpremultiplied image byte channels as an arithmetic vector.</summary>
    /// <param name="color">Framebuffer colour.</param>
    /// <returns>The RGB bytes, without converting to a five-bit or linear-light domain.</returns>
    private static Vector3 Rgb(Color color) => new(color.R8, color.G8, color.B8);

    /// <summary>Captures paired main and raw targets after a real production presentation.</summary>
    /// <param name="host">Active offscreen host.</param>
    /// <param name="root">Private output directory.</param>
    /// <param name="name">Capture pair name.</param>
    /// <returns>The main framebuffer image owned by the caller.</returns>
    private static async Task<Image> CaptureAsync(IBattleProbeHost host, string root, string name)
    {
        var image = await BattleProbeCapture.CaptureAsync(host, root, $"{name}-main.png");
        ProbeRequirement.Require(host.Renderer.SavePartnerView(Path.Combine(root, $"{name}-raw.png")) == Error.Ok, "blend fixture raw capture failed");
        return image;
    }
}
