using System.Numerics;

namespace Dw3Production.Presentation;

/// <summary>
/// Calculates painter-order keys for battle-model polygons. The layer has 4096
/// depth buckets. Farther keys render first and later registrations win when
/// keys match, which keeps overlapping polygons stable after mesh expansion.
/// </summary>
public static class RenderOrderingTable
{
    public const int DepthBucketShift = 12;
    public const int DepthBucketCount = 0x1000;
    public const int TriangleDepthScale = 0x555;
    public const int QuadDepthScale = 0x400;

    /// <summary>Returns the number of low depth bits omitted by the bucket key.</summary>
    public static int DepthShift(int bucketShift) => 14 - bucketShift;

    /// <summary>Builds a depth key from three already projected vertex depths.</summary>
    public static int TriangleKey(int sz0, int sz1, int sz2, int shift) =>
        (((sz0 >> shift) + (sz1 >> shift) + (sz2 >> shift)) * TriangleDepthScale) >> 12;

    /// <summary>Builds a depth key from four already projected vertex depths.</summary>
    public static int QuadKey(int sz0, int sz1, int sz2, int sz3, int shift) =>
        (((sz0 >> shift) + (sz1 >> shift) + (sz2 >> shift) + (sz3 >> shift)) * QuadDepthScale) >> 12;

    /// <summary>Accepts non-degenerate clockwise projected polygons.</summary>
    public static bool FacesCamera(Vector2 a, Vector2 b, Vector2 c, Vector2? d = null)
    {
        if (a == b || a == c || b == c || (d is { } fourth && (a == fourth || b == fourth || c == fourth)))
        {
            return false;
        }

        var signedArea = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
        return signedArea > 0;
    }

    /// <summary>Returns polygon indices in painter order.</summary>
    public static int[] DrawOrder(IReadOnlyList<int> keys) =>
        [.. Enumerable.Range(0, keys.Count).OrderByDescending(i => keys[i]).ThenByDescending(i => i)];

    /// <summary>Expands one already ordered quad into two triangles.</summary>
    public static ReadOnlySpan<int> WholeQuadIndices() => [0, 1, 2, 2, 1, 3];
}
