using System.Numerics;

namespace Dw3Production.Simulation;

/// <summary>
/// An NPC or obstacle on the field whose diamond footprint blocks the player. Width is the
/// source half-width of the diamond in world units (the full extent spans ±Width/2 on X and
/// ±Width/4 on Y). The collision shape matches the original diamond; only the player's
/// footprint is adapted (see <see cref="Locomotion"/>).
/// </summary>
public sealed record Occupant(string Name, Vector2 Position, int Width)
{
    /// <summary>
    /// Returns true when <paramref name="position"/> is inside this occupant's source diamond.
    /// Reconstructs FIELDSTG_checkActorFlightProbes: the diamond is axis-aligned with half-width
    /// on X and half-width/2 on Y, inscribed in the bounding box.
    /// </summary>
    /// <param name="position">World position to test.</param>
    /// <returns>True when the point is blocked by this occupant's diamond.</returns>
    public bool Blocks(Vector2 position)
    {
        var halfWidth = Width / 2f;
        var left = Position.X - halfWidth;
        var right = Position.X + halfWidth;
        var top = Position.Y - halfWidth / 2f;
        var bottom = Position.Y + halfWidth / 2f;
        if (position.X < left || position.X > right || position.Y < top || position.Y > bottom)
        {
            return false;
        }

        var dx = position.X - left;
        var dy = position.Y - top;
        var width = right - left;
        var height = bottom - top;
        var halfW = width / 2f;
        var halfH = height / 2f;
        if (halfW < dx)
        {
            dx = halfW - (dx - halfW);
        }

        if (halfH < dy)
        {
            dy = halfH - (dy - halfH);
        }

        return dx >= halfW - dy * (width / height);
    }

    /// <summary>
    /// Distance from <paramref name="point"/> to the diamond's boundary (0 when inside). Used by
    /// the rounded player circle to test circle-vs-diamond overlap without re-probing the
    /// source diamond with discrete points.
    /// </summary>
    /// <param name="point">World position to measure from.</param>
    /// <returns>Distance to the nearest boundary edge, or 0 when the point is inside the diamond.</returns>
    public float DistanceTo(Vector2 point)
    {
        if (Blocks(point))
        {
            return 0f;
        }

        var a = Width / 2f;   // half-width
        var b = a / 2f;       // half-height
        var cx = Position.X;
        var cy = Position.Y;
        var best = DistanceToSegment(point, new Vector2(cx + a, cy), new Vector2(cx, cy + b));
        best = MathF.Min(best, DistanceToSegment(point, new Vector2(cx, cy + b), new Vector2(cx - a, cy)));
        best = MathF.Min(best, DistanceToSegment(point, new Vector2(cx - a, cy), new Vector2(cx, cy - b)));
        best = MathF.Min(best, DistanceToSegment(point, new Vector2(cx, cy - b), new Vector2(cx + a, cy)));
        return best;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 segmentA, Vector2 segmentB)
    {
        var ab = segmentB - segmentA;
        var lengthSq = ab.LengthSquared();
        if (lengthSq <= 1e-12f)
        {
            return Vector2.Distance(point, segmentA);
        }

        var t = Math.Clamp(Vector2.Dot(point - segmentA, ab) / lengthSq, 0f, 1f);
        return Vector2.Distance(point, segmentA + ab * t);
    }
}
