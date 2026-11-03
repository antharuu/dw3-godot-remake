using Godot;
using Dw3Production.Presentation;
using System.Reflection;
using System.Runtime.CompilerServices;

/// <summary>
/// Standalone Godot scene that synthesises a 2×1 pixel texture per ABR blend mode
/// and compares the captured colours against expected values.
/// Used as a headless regression probe for the water-blend shader pipeline;
/// exits 0 on pass, 1 on colour mismatch, 2 on exception.
/// </summary>
public partial class LeadWaterProbe : Node2D
{
    /// <summary>Runs the ABR blend probe: renders four sprites, captures a screenshot, and asserts pixel colours.</summary>
    public override async void _Ready()
    {
        try
        {
            var renderer = (FieldRenderer)RuntimeHelpers.GetUninitializedObject(typeof(FieldRenderer));
            typeof(FieldRenderer).GetField("_waterBlendMaterials", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(renderer, new Dictionary<int, ShaderMaterial>());
            var method = typeof(FieldRenderer).GetMethod("WaterBlendMaterial", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var background = new Color(0.4f, 0.6f, 0.8f, 1f);
            AddChild(new ColorRect { Position = Vector2.Zero, Size = new Vector2(400, 200), Color = background });
            var test = Image.CreateEmpty(2, 1, false, Image.Format.Rgba8);
            test.SetPixel(0, 0, new Color(0.2f, 0.4f, 0.6f, 1f));
            test.SetPixel(1, 0, new Color(0, 0, 0, 0));
            var texture = ImageTexture.CreateFromImage(test);
            foreach (var abr in new[] { 0, 1, 2, 3 })
            {
                AddChild(new Sprite2D { Texture = texture, Centered = false, Position = new Vector2(20 + abr * 80, 20), Scale = new Vector2(25, 25), Material = (ShaderMaterial)method.Invoke(renderer, [abr])! });
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = GetViewport().GetTexture().GetImage();
            var output = System.Environment.GetEnvironmentVariable("DW3_CAPTURE_DIR")!;
            System.IO.Directory.CreateDirectory(output);
            image.SavePng(System.IO.Path.Combine(output, "synthetic-mask-abr.png"));
            var failures = 0;
            var expectedForeground = new Dictionary<int, Color>
            {
                [0] = new Color(0.3f, 0.5f, 0.7f, 1f),
                [1] = new Color(0.6f, 1f, 1f, 1f),
                [2] = new Color(0.2f, 0.2f, 0.2f, 1f),
                [3] = new Color(0.45f, 0.7f, 0.95f, 1f),
            };
            foreach (var abr in new[] { 0, 1, 2, 3 })
            {
                var foreground = image.GetPixel(20 + abr * 80 + 10, 30);
                var transparent = image.GetPixel(20 + abr * 80 + 35, 30);
                var transparentPass = Math.Abs(transparent.R - background.R) < 0.012 && Math.Abs(transparent.G - background.G) < 0.012 && Math.Abs(transparent.B - background.B) < 0.012;
                var expected = expectedForeground[abr];
                var foregroundPass = Math.Abs(foreground.R - expected.R) < 0.025 && Math.Abs(foreground.G - expected.G) < 0.025 && Math.Abs(foreground.B - expected.B) < 0.025;
                GD.Print($"LEAD_MASK_TEST abr={abr} transparent_texel={transparent} expected_background={background} transparent_pass={transparentPass} foreground={foreground} expected_foreground={expected} foreground_pass={foregroundPass}");
                if (!transparentPass || !foregroundPass) failures++;
            }
            GetTree().Quit(failures == 0 ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            GetTree().Quit(2);
        }
    }
}
