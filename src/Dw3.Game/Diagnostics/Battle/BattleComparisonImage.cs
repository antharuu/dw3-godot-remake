using Dw3Production.Diagnostics.Field;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Normalizes and composes battle screenshots for source-image comparisons.</summary>
internal static class BattleComparisonImage
{
    private static readonly Dictionary<char, string[]> LabelGlyphs = new()
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

    /// <summary>Converts a viewport screenshot to the original 320x240 picture area.</summary>
    /// <param name="image">A 1280x720 viewport or already-normalized 320x240 image.</param>
    /// <returns>An RGBA8 320x240 copy.</returns>
    public static Image ToSourceSize(Image image)
    {
        var copy = (Image)image.Duplicate();
        copy.Convert(Image.Format.Rgba8);
        if (copy.GetSize() == new Vector2I(1280, 720))
        {
            copy = copy.GetRegion(new Rect2I(160, 0, 960, 720));
            copy.Resize(320, 240, Image.Interpolation.Nearest);
        }

        ProbeRequirement.Require(
            copy.GetSize() == new Vector2I(320, 240),
            $"expected source-size image, got {copy.GetWidth()}x{copy.GetHeight()}");
        return copy;
    }

    /// <summary>Formats image bounds as integer source coordinates for stable probe logs.</summary>
    /// <param name="rectangle">Measured image bounds.</param>
    /// <returns>A parenthesized x, y, width and height tuple.</returns>
    public static string FormatBounds(Rect2 rectangle) =>
        $"({rectangle.Position.X:0},{rectangle.Position.Y:0},{rectangle.Size.X:0},{rectangle.Size.Y:0})";

    /// <summary>Places remake and source images beside each other with compact pixel labels.</summary>
    /// <param name="remake">Left image.</param>
    /// <param name="source">Right image.</param>
    /// <param name="leftLabel">Uppercase left label.</param>
    /// <param name="rightLabel">Uppercase right label.</param>
    /// <returns>The composed RGBA8 image.</returns>
    public static Image SideBySide(Image remake, Image source, string leftLabel, string rightLabel)
    {
        var left = (Image)remake.Duplicate();
        var right = (Image)source.Duplicate();
        left.Convert(Image.Format.Rgba8);
        right.Convert(Image.Format.Rgba8);
        var output = Image.CreateEmpty(
            left.GetWidth() + right.GetWidth(),
            Math.Max(left.GetHeight(), right.GetHeight()),
            false,
            Image.Format.Rgba8);
        output.Fill(Colors.Black);
        output.BlitRect(left, new Rect2I(Vector2I.Zero, left.GetSize()), Vector2I.Zero);
        output.BlitRect(right, new Rect2I(Vector2I.Zero, right.GetSize()), new Vector2I(left.GetWidth(), 0));
        DrawLabel(output, leftLabel, new Vector2I(8, 7));
        DrawLabel(output, rightLabel, new Vector2I(left.GetWidth() + 8, 7));
        return output;
    }

    /// <summary>Draws one compact uppercase label without depending on a runtime font.</summary>
    /// <param name="image">Comparison image to modify.</param>
    /// <param name="label">Uppercase label.</param>
    /// <param name="origin">Top-left output pixel.</param>
    private static void DrawLabel(Image image, string label, Vector2I origin)
    {
        var x = origin.X;
        foreach (var character in label)
        {
            if (!LabelGlyphs.TryGetValue(character, out var glyph))
            {
                continue;
            }

            DrawGlyph(image, glyph, new Vector2I(x, origin.Y));
            x += 12;
        }
    }

    /// <summary>Rasterizes one 5x7 label glyph at two output pixels per source bit.</summary>
    /// <param name="image">Comparison image to modify.</param>
    /// <param name="glyph">Seven rows of five binary characters.</param>
    /// <param name="origin">Top-left output pixel.</param>
    private static void DrawGlyph(Image image, string[] glyph, Vector2I origin)
    {
        for (var y = 0; y < glyph.Length; y++)
        {
            for (var x = 0; x < glyph[y].Length; x++)
            {
                if (glyph[y][x] == '1')
                {
                    DrawGlyphPixel(image, origin, x, y);
                }
            }
        }
    }

    /// <summary>Writes the 2x2 output block for one set glyph bit.</summary>
    /// <param name="image">Comparison image to modify.</param>
    /// <param name="origin">Glyph top-left output pixel.</param>
    /// <param name="glyphX">Glyph column in 0..4.</param>
    /// <param name="glyphY">Glyph row in 0..6.</param>
    private static void DrawGlyphPixel(Image image, Vector2I origin, int glyphX, int glyphY)
    {
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                image.SetPixel(
                    origin.X + (glyphX * 2) + x,
                    origin.Y + (glyphY * 2) + y,
                    Colors.White);
            }
        }
    }
}
