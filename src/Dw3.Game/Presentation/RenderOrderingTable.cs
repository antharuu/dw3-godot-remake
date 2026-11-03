using System.Numerics;

namespace Dw3Production.Presentation;

/// <summary>
/// Calculates painter-order keys for battle-model polygons. The layer has 4096
/// depth buckets. Farther keys render first and later registrations win when
/// keys match, which keeps overlapping polygons stable after mesh expansion.
/// </summary>
public static class RenderOrderingTable
{
    /// <summary>Bit position used to select one of the 4096 painter-order buckets.</summary>
    public const int DepthBucketShift = 12;

    /// <summary>Number of discrete depth buckets available to the ordering pass.</summary>
    public const int DepthBucketCount = 0x1000;

    /// <summary>Fixed-point reciprocal of three, used when averaging triangle depth samples.</summary>
    public const int TriangleDepthScale = 0x555;

    /// <summary>Fixed-point reciprocal of four, used when averaging quad depth samples.</summary>
    public const int QuadDepthScale = 0x400;

    /// <summary>Converts a bucket bit position into the number of discarded low depth bits.</summary>
    /// <param name="bucketShift">Bit position used by the ordering-table bucket index.</param>
    /// <returns>The number of low bits omitted before combining projected depths.</returns>
    public static int DepthShift(int bucketShift) => 14 - bucketShift;

    /// <summary>Averages three projected depths into the fixed-point key used for a triangle.</summary>
    /// <param name="sz0">Projected depth of the first triangle vertex.</param>
    /// <param name="sz1">Projected depth of the second triangle vertex.</param>
    /// <param name="sz2">Projected depth of the third triangle vertex.</param>
    /// <param name="shift">Number of low bits discarded from every projected depth.</param>
    /// <returns>A bucket-compatible painter-order key for the triangle.</returns>
    public static int TriangleKey(int sz0, int sz1, int sz2, int shift) =>
        (((sz0 >> shift) + (sz1 >> shift) + (sz2 >> shift)) * TriangleDepthScale) >> 12;

    /// <summary>Averages four projected depths into the fixed-point key used for a quad.</summary>
    /// <param name="sz0">Projected depth of the first quad vertex.</param>
    /// <param name="sz1">Projected depth of the second quad vertex.</param>
    /// <param name="sz2">Projected depth of the third quad vertex.</param>
    /// <param name="sz3">Projected depth of the fourth quad vertex.</param>
    /// <param name="shift">Number of low bits discarded from every projected depth.</param>
    /// <returns>A bucket-compatible painter-order key for the quad.</returns>
    public static int QuadKey(int sz0, int sz1, int sz2, int sz3, int shift) =>
        (((sz0 >> shift) + (sz1 >> shift) + (sz2 >> shift) + (sz3 >> shift)) * QuadDepthScale) >> 12;

    /// <summary>Checks whether the first projected triangle has positive winding and no repeated vertices.</summary>
    /// <param name="a">First projected polygon vertex.</param>
    /// <param name="b">Second projected polygon vertex.</param>
    /// <param name="c">Third projected polygon vertex.</param>
    /// <param name="d">Optional fourth quad vertex, checked only for degeneracy.</param>
    /// <returns><see langword="true"/> when the polygon is non-degenerate and faces the camera.</returns>
    public static bool FacesCamera(Vector2 a, Vector2 b, Vector2 c, Vector2? d = null)
    {
        if (a == b || a == c || b == c || (d is { } fourth && (a == fourth || b == fourth || c == fourth)))
        {
            return false;
        }

        var signedArea = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
        return signedArea > 0;
    }

    /// <summary>Orders polygon indices from farthest to nearest, preserving reverse submission order for ties.</summary>
    /// <param name="keys">One projected-depth key per polygon.</param>
    /// <returns>All indices of <paramref name="keys"/> in painter submission order.</returns>
    public static int[] DrawOrder(IReadOnlyList<int> keys) =>
        [.. Enumerable.Range(0, keys.Count).OrderByDescending(i => keys[i]).ThenByDescending(i => i)];

    /// <summary>Provides the six local indices that split a submitted quad into two triangles.</summary>
    /// <returns>A six-element span with the stable quad triangulation.</returns>
    public static ReadOnlySpan<int> WholeQuadIndices() => [0, 1, 2, 2, 1, 3];
}
