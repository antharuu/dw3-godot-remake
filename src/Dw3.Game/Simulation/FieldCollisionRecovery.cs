using System.Numerics;

namespace Dw3Production.Simulation;

/// <summary>Recovers a player circle that a completed climb or drop left overlapping void floor.</summary>
public static class FieldCollisionRecovery
{
    private static readonly (int X, int Y)[] SearchDirections =
        [(0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>Finds the nearest free candidate in the traversal exit search order.</summary>
    /// <param name="floor">Active field collision map.</param>
    /// <param name="position">Player center in field world units.</param>
    /// <returns>The original center when free, otherwise the first free candidate.</returns>
    public static Vector2 Depenetrate(FloorMap floor, Vector2 position)
    {
        if (CircleFreeAt(floor, position))
        {
            return position;
        }

        int limit = (int)MathF.Ceiling(Locomotion.PlayerCollisionRadius) + 1;
        for (int distance = 1; distance <= limit; distance++)
        {
            Vector2? candidate = FreeCandidateAtDistance(floor, position, distance);
            if (candidate is not null)
            {
                return candidate.Value;
            }
        }

        return position;
    }

    /// <summary>Tests whether the player circle stays in bounds and avoids every void cell.</summary>
    /// <param name="floor">Active field collision map.</param>
    /// <param name="position">Player center in field world units.</param>
    /// <returns>True when no void square touches the circle.</returns>
    public static bool CircleFreeAt(FloorMap floor, Vector2 position)
    {
        float radius = Locomotion.PlayerCollisionRadius;
        if (!CircleInsideMap(floor, position, radius))
        {
            return false;
        }

        return !CellsAround(position, radius, floor)
            .Any(cell => CellBlocksCircle(floor, position, radius, cell.X, cell.Y));
    }

    /// <summary>Checks all candidates at one integer displacement.</summary>
    /// <param name="floor">Active field collision map.</param>
    /// <param name="position">Blocked center.</param>
    /// <param name="distance">Candidate displacement in world units.</param>
    /// <returns>The first free candidate, or null.</returns>
    private static Vector2? FreeCandidateAtDistance(FloorMap floor, Vector2 position, int distance)
    {
        foreach ((int x, int y) in SearchDirections)
        {
            var candidate = new Vector2(position.X + (x * distance), position.Y + (y * distance));
            if (CircleFreeAt(floor, candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Checks the circle's bounding box against map dimensions.</summary>
    /// <param name="floor">Active field collision map.</param>
    /// <param name="position">Circle center.</param>
    /// <param name="radius">Circle radius in world units.</param>
    /// <returns>True when the complete circle lies inside the map.</returns>
    private static bool CircleInsideMap(FloorMap floor, Vector2 position, float radius) =>
        position.X - radius >= 0f
        && position.Y - radius >= 0f
        && position.X + radius <= floor.Width
        && position.Y + radius <= floor.Height;

    /// <summary>Enumerates the integer cells intersecting the circle's bounding box.</summary>
    /// <param name="position">Circle center.</param>
    /// <param name="radius">Circle radius in world units.</param>
    /// <param name="floor">Active field collision map.</param>
    /// <returns>Candidate cell coordinates.</returns>
    private static IEnumerable<(int X, int Y)> CellsAround(Vector2 position, float radius, FloorMap floor)
    {
        int minX = Math.Max(0, (int)MathF.Floor(position.X - radius));
        int maxX = Math.Min(floor.Width - 1, (int)MathF.Ceiling(position.X + radius));
        int minY = Math.Max(0, (int)MathF.Floor(position.Y - radius));
        int maxY = Math.Min(floor.Height - 1, (int)MathF.Ceiling(position.Y + radius));
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                yield return (x, y);
            }
        }
    }

    /// <summary>Tests a void cell's unit square against the player circle.</summary>
    /// <param name="floor">Active field collision map.</param>
    /// <param name="position">Circle center.</param>
    /// <param name="radius">Circle radius in world units.</param>
    /// <param name="x">Cell column.</param>
    /// <param name="y">Cell row.</param>
    /// <returns>True when a void cell touches the circle.</returns>
    private static bool CellBlocksCircle(FloorMap floor, Vector2 position, float radius, int x, int y)
    {
        if (floor.CellAt(new Vector2(x, y)) != 0)
        {
            return false;
        }

        var closest = new Vector2(Math.Clamp(position.X, x, x + 1f), Math.Clamp(position.Y, y, y + 1f));
        return Vector2.DistanceSquared(position, closest) <= radius * radius;
    }
}
