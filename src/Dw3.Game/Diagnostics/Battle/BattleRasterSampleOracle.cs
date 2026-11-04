using System.Text.Json;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Checks GPU samples using independently interpolated input texels and byte-domain arithmetic.</summary>
internal static class BattleRasterSampleOracle
{
    /// <summary>Requires a recorded source identity, independent expected channels and a failing wrong-blend control.</summary>
    /// <param name="vector">Externally selected interior coordinate, not an expected framebuffer colour.</param>
    /// <param name="primitive">Actual current collected source primitive.</param>
    /// <param name="root">Directory containing paired destination, actual and wrong-blend captures.</param>
    public static void Verify(JsonElement vector, BattleRasterPrimitive primitive, string root)
    {
        var name = vector.GetProperty("name").GetString();
        var view = vector.GetProperty("view").GetString();
        var x = vector.GetProperty("x").GetInt32();
        var y = vector.GetProperty("y").GetInt32();
        ProbeRequirement.Require(
            primitive.Fighter == vector.GetProperty("fighter").GetInt32()
            && primitive.Polygon.Bone == vector.GetProperty("bone").GetInt32()
            && primitive.Polygon.VertexStart == vector.GetProperty("vertexStart").GetInt32()
            && primitive.Polygon.TextureKey == vector.GetProperty("textureKey").GetInt32()
            && primitive.Polygon.Abr == vector.GetProperty("runtimeAbr").GetInt32(),
            "sample source identity drifted");
        var (texel, foreground) = Sample(vector, primitive, new Vector2(x + 0.5f, y + 0.5f));
        using var destination = Image.LoadFromFile(Path.Combine(root, $"{name}-destination-{view}.png"));
        using var actual = Image.LoadFromFile(Path.Combine(root, $"{name}-actual-{view}.png"));
        using var wrong = Image.LoadFromFile(Path.Combine(root, $"{name}-wrong-{view}.png"));
        var background = Rgb(destination.GetPixel(x, y));
        using var noOpponent = Image.LoadFromFile(Path.Combine(root, $"{name}-no-opponent-{view}.png"));
        ProbeRequirement.Require(Error(background, Rgb(noOpponent.GetPixel(x, y))) > 2, $"opponent does not contribute at selected overlap {name}");
        var expected = Expected(background, foreground, texel.A8, primitive.Polygon.Abr);
        var pixel = Rgb(actual.GetPixel(x, y));
        var error = Error(pixel, expected);
        ProbeRequirement.Require(error <= 2, $"real STP sample {name} actual={pixel} expected={expected} error={error}");
        if (texel.A8 == 128 && primitive.Polygon.Abr >= 0)
        {
            ProbeRequirement.Require(Error(Rgb(wrong.GetPixel(x, y)), expected) > 2, $"wrong blend did not fail real sample {name}");
        }

        var word = (texel.R8 >> 3) | ((texel.G8 >> 3) << 5) | ((texel.B8 >> 3) << 10) | (texel.A8 == 128 ? 0x8000 : 0);
        QuietLog.Info($"battle-real-stp sample={name} fighter={primitive.Fighter} bone={primitive.Polygon.Bone} vertex={primitive.Polygon.VertexStart} key={primitive.Key} texture={primitive.Polygon.TextureKey} storedABR={primitive.Polygon.Abr + 1} runtimeABR={primitive.Polygon.Abr} rawWord=0x{word:X4} alpha={texel.A8} destination={background} foreground={foreground} expected={expected} actual={pixel} maxError8bit={error}");
    }

    /// <summary>Interpolates the selected original source triangle and nearest texel without using the compositor helper.</summary>
    /// <param name="vector">Interior coordinate and source triangle indices.</param>
    /// <param name="primitive">Current projected source corners and mutable texture.</param>
    /// <param name="point">Framebuffer pixel centre.</param>
    /// <returns>The actual input texel and independently modulated, saturated foreground.</returns>
    private static (Color Texel, Vector3 Foreground) Sample(JsonElement vector, BattleRasterPrimitive primitive, Vector2 point)
    {
        var indices = vector.GetProperty("triangle").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var a = primitive.Screen[indices[0]];
        var edge1 = primitive.Screen[indices[1]] - a;
        var edge2 = primitive.Screen[indices[2]] - a;
        var relative = point - a;
        var denominator = edge1.Cross(edge2);
        var b = relative.Cross(edge2) / denominator;
        var c = edge1.Cross(relative) / denominator;
        var weights = new[] { 1 - b - c, b, c };
        ProbeRequirement.Require(weights.Min() > 0.05f, "sample lies on a triangle boundary");
        var uv = Vector2.Zero;
        var color = Vector3.Zero;
        for (var i = 0; i < indices.Length; i++)
        {
            uv += primitive.Polygon.Uvs[indices[i]] * weights[i];
            var vertex = primitive.Polygon.Colors[indices[i]];
            color += new Vector3(vertex.R, vertex.G, vertex.B) * weights[i];
        }

        using var image = primitive.Texture.GetImage();
        var x = Mathf.PosMod((int)MathF.Floor(uv.X * image.GetWidth()), image.GetWidth());
        var y = Mathf.PosMod((int)MathF.Floor(uv.Y * image.GetHeight()), image.GetHeight());
        var texel = image.GetPixel(x, y);
        var expectedTexel = vector.GetProperty("texel").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        ProbeRequirement.Require(texel.R8 == expectedTexel[0] && texel.G8 == expectedTexel[1] && texel.B8 == expectedTexel[2] && texel.A8 == expectedTexel[3], "sample texel or raw alpha drifted");
        return (texel, (Rgb(texel) * color * 2).Clamp(Vector3.Zero, new Vector3(255, 255, 255)));
    }

    /// <summary>Evaluates only the independent byte-domain blend equation, not the helper under test.</summary>
    /// <param name="background">Observed prefix destination.</param>
    /// <param name="foreground">Interpolated and saturated source RGB.</param>
    /// <param name="alpha">Raw decoded transparency classification.</param>
    /// <param name="mode">Observed runtime ABR selection.</param>
    /// <returns>Expected channels before framebuffer integer rounding.</returns>
    private static Vector3 Expected(Vector3 background, Vector3 foreground, int alpha, int mode)
    {
        if (alpha == 0)
        {
            return background;
        }

        if (alpha == 255 || mode < 0)
        {
            return foreground;
        }

        return mode == 0 ? (background + foreground) / 2 : (background + foreground).Clamp(Vector3.Zero, new Vector3(255, 255, 255));
    }

    /// <summary>Converts framebuffer channels to eight-bit arithmetic units without gamma conversion.</summary>
    /// <param name="color">Texture or framebuffer value.</param>
    /// <returns>The RGB bytes as a vector.</returns>
    private static Vector3 Rgb(Color color) => new(color.R8, color.G8, color.B8);

    /// <summary>Measures maximum absolute channel error before final integer rounding.</summary>
    /// <param name="actual">Observed byte channels.</param>
    /// <param name="expected">Independent floating-point arithmetic result.</param>
    /// <returns>Error in eight-bit units, not a five-bit PSX parity claim.</returns>
    private static float Error(Vector3 actual, Vector3 expected)
    {
        var error = (actual - expected).Abs();
        return Math.Max(error.X, Math.Max(error.Y, error.Z));
    }
}
