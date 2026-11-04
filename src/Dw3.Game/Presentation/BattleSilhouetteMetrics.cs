using Godot;

namespace Dw3Production.Presentation;

/// <summary>Measures one fighter silhouette in the original 320x240 logical battle raster.</summary>
/// <param name="Bounds">Axis-aligned source-raster silhouette bounds.</param>
/// <param name="Anchor">Bottom-centre point used to compare placement across models.</param>
/// <param name="WidthFraction">Silhouette width divided by the logical 320-pixel raster width.</param>
/// <param name="HeightFraction">Silhouette height divided by the logical 240-pixel raster height.</param>
public readonly record struct BattleSilhouetteMetrics(
    Rect2 Bounds,
    Vector2 Anchor,
    float WidthFraction,
    float HeightFraction)
{
    /// <summary>Measures a non-empty silhouette without mixing it with the 16:9 presentation margins.</summary>
    /// <param name="bounds">Silhouette bounds after conversion to the original logical raster.</param>
    /// <returns>Bounds, bottom-centre anchor, and raster-relative proportions.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bounds"/> has no area.</exception>
    public static BattleSilhouetteMetrics FromBounds(Rect2 bounds)
    {
        if (bounds.Size.X <= 0f || bounds.Size.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "silhouette bounds must have positive area");
        }

        var anchor = new Vector2(bounds.GetCenter().X, bounds.End.Y);
        return new BattleSilhouetteMetrics(bounds, anchor, bounds.Size.X / 320f, bounds.Size.Y / 240f);
    }
}
