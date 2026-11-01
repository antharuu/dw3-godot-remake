using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The procedural hit-spark sprite: a 16x16 radial pale-yellow disc fading to transparent at
/// radius 7.5 texels. It stands in for the source hit-effect sheet entry, which is not decoded.
/// </summary>
internal static class BattleHitSparkTexture
{
    private const int Size = 16;
    private const float Radius = 7.5f;

    /// <summary>Renders the spark image.</summary>
    /// <returns>A new texture.</returns>
    public static ImageTexture Create()
    {
        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var dx = x - Radius;
                var dy = y - Radius;
                var d = Mathf.Sqrt((dx * dx) + (dy * dy));
                var alpha = Mathf.Clamp(1f - (d / Radius), 0f, 1f);
                image.SetPixel(x, y, new Color(1, 1, 0.85f, alpha));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
