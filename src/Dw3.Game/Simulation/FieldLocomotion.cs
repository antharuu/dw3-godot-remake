using System.Numerics;

namespace Dw3Production.Simulation;

public sealed class FloorMap
{
    private readonly byte[] _cells;

    public FloorMap(int width, int height, byte[] cells)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (cells.Length != checked(width * height)) throw new ArgumentException("floor cell count does not match dimensions", nameof(cells));
        Width = width;
        Height = height;
        _cells = cells.ToArray();
    }

    public int Width { get; }
    public int Height { get; }

    public bool Contains(Vector2 position) => position.X >= 0 && position.Y >= 0 && position.X < Width && position.Y < Height;

    public byte CellAt(Vector2 position)
    {
        if (!Contains(position)) throw new ArgumentOutOfRangeException(nameof(position));
        // The source map lookup truncates its fixed-point tile position with >> 8.
        var x = Math.Clamp((int)position.X, 0, Width - 1);
        var y = Math.Clamp((int)position.Y, 0, Height - 1);
        return _cells[y * Width + x];
    }
}

public sealed record Occupant(string Name, Vector2 Position, int Width)
{
    public bool Blocks(Vector2 position)
    {
        var halfWidth = Width / 2f;
        var left = Position.X - halfWidth;
        var right = Position.X + halfWidth;
        var top = Position.Y - halfWidth / 2f;
        var bottom = Position.Y + halfWidth / 2f;
        if (position.X < left || position.X > right || position.Y < top || position.Y > bottom) return false;

        var dx = position.X - left;
        var dy = position.Y - top;
        var width = right - left;
        var height = bottom - top;
        var halfW = width / 2f;
        var halfH = height / 2f;
        if (halfW < dx) dx = halfW - (dx - halfW);
        if (halfH < dy) dy = halfH - (dy - halfH);
        return dx >= halfW - dy * (width / height);
    }

    /// <summary>Distance from a point to the diamond's boundary (0 when inside), used by
    /// the rounded player circle to test circle-vs-diamond overlap without re-probing the
    /// source diamond with discrete points.</summary>
    public float DistanceTo(Vector2 point)
    {
        if (Blocks(point)) return 0f;
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
        if (lengthSq <= 1e-12f) return Vector2.Distance(point, segmentA);
        var t = Math.Clamp(Vector2.Dot(point - segmentA, ab) / lengthSq, 0f, 1f);
        return Vector2.Distance(point, segmentA + ab * t);
    }
}

/// <summary>
/// The player's movement on the field: maps a SemanticDirection input to a displacement
/// in world coordinates using the disc's per-slope direction-vector table (FIELD_MAP_AREAS
/// floor cells 0..6), at the source 50 Hz PAL speed scaled to the 60 Hz simulation tick.
/// The player's collision footprint is a circle of fixed radius replacing the original
/// octagon (intentional adaptation: natural wall-slide with no corner stalls).
/// Engine-free; tested against fabricated floor maps.
/// </summary>
public sealed class Locomotion
{
    // The EU version moves the player at 0x4CC/256 = 4.796875 world units per PAL task
    // update at 50 Hz. The core simulates at 60 Hz, hence the 50/60 conversion.
    public const float SourceRunWorldUnitsPerSimulationTick = (0x4CC / 256f) * (50f / 60f);
    public const float SourceWalkWorldUnitsPerSimulationTick = SourceRunWorldUnitsPerSimulationTick / 4f;
    public const float SourceRunWorldUnitsPerSecond = SourceRunWorldUnitsPerSimulationTick * 60f;

    // M2 comfort adaptation (documented, not source fidelity): the source footprint is a
    // discrete diamond of five direction-specific leading-edge probes (offsets +-8/+-6)
    // derived from FIELDSTG_checkFlightProbes. Its convex corners catch on protruding void
    // cells, so a held diagonal still stalls against a staircase wall despite the tangent
    // slide. This replaces the diamond with a circle of equal cardinal half-extent (+-8).
    // A circle has no corner, so a wall contact always yields a natural tangent. It is
    // inscribed in the former square half-extent, so it never reaches beyond the source
    // +-8 cardinal reach and no obstacle is weakened.
    public const float PlayerCollisionRadius = 8f;

    // The removed source diamond's octagon outline (world offsets from the actor centre),
    // retained only for the F3 comparison rendering. The active shape is the circle above.
    public static readonly Vector2[] SourceDiamondOutline =
    [
        new(-6, -8), new(6, -8), new(8, -6), new(8, 6), new(6, 8), new(-6, 8), new(-8, 6), new(-8, -6),
    ];

    private static readonly int[] MirrorDirections = [0, 7, 6, 5, 4, 3, 2, 1];
    private static readonly Vector2[,] DirectionVectors =
    {
        { new(0, 2048), new(-2896, 1448), new(-4096, 0), new(-2896, -1448), new(0, -2048), new(2896, -1448), new(4096, 0), new(2896, 1448) },
        { new(599, 3405), new(-2048, 3368), new(-3496, 1358), new(-2896, -1448), new(-600, -3405), new(2048, -3368), new(3495, -1358), new(2896, 1448) },
        { new(155, 2865), new(-2676, 2606), new(-3940, 818), new(-2896, -1448), new(-156, -2866), new(2676, -2606), new(3939, -819), new(2896, 1448) },
        { new(68, 2616), new(-2799, 2254), new(-4027, 569), new(-2896, -1448), new(-69, -2618), new(2797, -2254), new(4026, -571), new(2896, 1448) },
        { new(273, 3081), new(-2509, 2911), new(-3822, 1034), new(-2896, -1448), new(-274, -3082), new(2509, -2911), new(3821, -1035), new(2896, 1448) },
        { new(344, 3178), new(-2409, 3047), new(-3751, 1130), new(-2896, -1448), new(-345, -3179), new(2409, -2254), new(3750, -1132), new(2896, 1448) },
        { new(9, 2271), new(-2882, 1764), new(-4086, 224), new(-2896, -1448), new(-10, -2272), new(2882, -1764), new(4085, -225), new(2896, 1448) },
    };

    /// <summary>
    /// Attempts one fixed movement step against the supplied floor and occupants. It updates facing
    /// and animation on every input, updates position only for an accepted full or permitted slide
    /// step, and returns the exact accepted displacement or rejection without mutating map data.
    /// </summary>
    public MovementResult Tick(GameState state, SemanticDirection input, FloorMap map, IReadOnlyList<Occupant> occupants)
    {
        if (input == SemanticDirection.None)
        {
            state.AnimationSet = 1;
            state.AnimationDirection = state.Facing;
            return MovementResult.Stopped;
        }

        var direction = (int)input;
        // SemanticDirection is 0=up clockwise; the source FIELDSTG vectors, facing and
        // trigger table use 0=down counter-clockwise. Rebase once before any source use.
        var sourceDirection = (direction + 4) % 8;
        state.SetFacing(sourceDirection);
        state.AnimationDirection = sourceDirection;
        var cell = map.CellAt(state.Position);
        // The walk-step map lookup does not block cell 0: void (0) and
        // open (1) both resolve to slope row 0, so walking over void is legal. The
        // only rejection is an unclassified low nibble (slope row > 6).
        var row = cell & 0x0F;
        row -= row != 0 ? 1 : 0;
        if (row < 0 || row > 6) return MovementResult.Rejected($"unclassified-floor:{cell}");

        var mirror = (cell & 0x10) != 0;
        var effective = mirror ? MirrorDirections[sourceDirection] : sourceDirection;
        var vector = DirectionVectors[row, effective];
        // The source passes the complete 8.8 actor speed to getWalkStep for RUN and
        // one quarter for WALK. Convert its 50 Hz PAL task cadence to M1's fixed 60 Hz.
        var scale = state.IsRunning ? SourceRunWorldUnitsPerSimulationTick : SourceWalkWorldUnitsPerSimulationTick;
        var step = new Vector2(vector.X * scale * (mirror ? -1 : 1) / 4096f, vector.Y * scale / 4096f);
        if (!FootprintFreeAcrossStep(state.Position, step, map, occupants, out var rejection, out var contact))
        {
            // M2 comfort adaptation (documented, not source fidelity): before the M1
            // cardinal projection, project the rejected step onto the local smoothed
            // wall tangent so a diagonal or irregular (staircase) void contour glides
            // instead of catching on its convex corners. The tangent is derived from
            // the source void cells at the first swept contact point only; the swept
            // source probes and occupancy remain authoritative, so no obstacle is
            // weakened or crossed and the slide never exceeds the input step magnitude
            // (no hidden acceleration). Concave corners keep their wall-normal slide,
            // which the opposite wall then blocks, so a corner stays blocked exactly
            // like a frontal contact.
            if (rejection == "void-footprint")
            {
                var slideNormal = ComputeSurfaceNormal(map, contact);
                var intoWall = slideNormal == Vector2.Zero ? 0f : Vector2.Dot(step, slideNormal);
                if (intoWall < 0f)
                {
                    var tangentStep = step - slideNormal * intoWall;
                    if (tangentStep != Vector2.Zero &&
                        FootprintFreeAcrossStep(state.Position, tangentStep, map, occupants, out _, out _))
                    {
                        state.Position += tangentStep;
                        state.AnimationSet = state.IsRunning ? 5 : 4;
                        return MovementResult.Accepted(tangentStep, cell, row, mirror, sourceDirection);
                    }
                }
            }

            // M1's intentional comfort adaptation only projects a rejected diagonal
            // step onto one signed cardinal component, never adds distance or
            // depenetrates. This preserves obstacles, source scale and swept probes.
            var candidates = new[] { new Vector2(step.X, 0), new Vector2(0, step.Y) }
                .Where(candidate => candidate != Vector2.Zero)
                .OrderByDescending(candidate => candidate.LengthSquared());
            foreach (var candidate in candidates)
            {
                if (!FootprintFreeAcrossStep(state.Position, candidate, map, occupants, out _, out _)) continue;
                state.Position += candidate;
                state.AnimationSet = state.IsRunning ? 5 : 4;
                return MovementResult.Accepted(candidate, cell, row, mirror, sourceDirection);
            }
            return MovementResult.Rejected(rejection);
        }

        state.Position += step;
        state.AnimationSet = state.IsRunning ? 5 : 4;
        return MovementResult.Accepted(step, cell, row, mirror, sourceDirection);
    }

    // Radius (in source cells) over which the void contour is smoothed to derive the
    // local wall surface normal. Comparable to the source leading-edge probe reach
    // (+-8 cells), so a staircase boundary is sampled at the actor's own scale rather
    // than per protruding cell.
    private const int SurfaceSmoothRadius = 6;

    // Computes the outward (away-from-void) unit normal of the smoothed void contour
    // around a position, or Zero when no void lies in reach. The normal is the negated
    // gradient of a box-smoothed blocked density, so it follows a wall's average
    // orientation instead of individual staircase cells. Every value is derived from
    // the real source floor cells; nothing is invented.
    private static Vector2 ComputeSurfaceNormal(FloorMap map, Vector2 position)
    {
        var cx = (int)position.X;
        var cy = (int)position.Y;

        float BlockedDensity(int bx, int by)
        {
            var blocked = 0;
            var total = 0;
            for (var dy = -SurfaceSmoothRadius; dy <= SurfaceSmoothRadius; dy++)
            for (var dx = -SurfaceSmoothRadius; dx <= SurfaceSmoothRadius; dx++)
            {
                total++;
                var probe = new Vector2(bx + dx, by + dy);
                if (!map.Contains(probe) || map.CellAt(probe) == 0) blocked++;
            }
            return blocked / (float)total;
        }

        var gx = BlockedDensity(cx + SurfaceSmoothRadius, cy) - BlockedDensity(cx - SurfaceSmoothRadius, cy);
        var gy = BlockedDensity(cx, cy + SurfaceSmoothRadius) - BlockedDensity(cx, cy - SurfaceSmoothRadius);
        var gradient = new Vector2(gx, gy);
        return gradient == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(-gradient);
    }

    private static bool FootprintFreeAcrossStep(Vector2 start, Vector2 step, FloorMap map, IReadOnlyList<Occupant> occupants, out string rejection, out Vector2 contact)
    {
        // A source PAL run step is nearly five tiles. The 60 Hz conversion remains nearly
        // four, so inspect bounded intermediate footprints rather than tunnelling over a
        // wall or actor that lies between its endpoints.
        var substeps = Math.Max(1, (int)MathF.Ceiling(MathF.Max(MathF.Abs(step.X), MathF.Abs(step.Y))));
        for (var index = 1; index <= substeps; index++)
        {
            var next = start + step * (index / (float)substeps);
            if (!map.Contains(next))
            {
                rejection = "map-bound";
                contact = next;
                return false;
            }
            if (!FootprintFree(next, map, occupants, out rejection, out contact)) return false;
        }
        rejection = string.Empty;
        contact = Vector2.Zero;
        return true;
    }

    private static bool FootprintFree(Vector2 next, FloorMap map, IReadOnlyList<Occupant> occupants, out string rejection, out Vector2 contact)
    {
        // The source footprint (FIELDSTG_checkFlightProbes) is a discrete diamond of five
        // direction-specific leading-edge probes. Its convex corners catch on protruding
        // void cells, so even with the tangent slide a held diagonal stalls against a
        // staircase wall. The M2 comfort adaptation replaces it with a circle of equal
        // cardinal half-extent (PlayerCollisionRadius), which has no corner: a wall
        // contact always yields a natural tangent. The circle is inscribed in the former
        // square half-extent, so it never reaches beyond the +-8 source cardinal reach.
        // Obstacles (raw void and out-of-bounds) and occupancy (the unchanged source
        // diamond) stay authoritative, and the swept substeps prevent tunnelling.
        var radius = PlayerCollisionRadius;

        // The circle must stay entirely inside the floor bounds.
        if (next.X - radius < 0f || next.Y - radius < 0f ||
            next.X + radius > map.Width || next.Y + radius > map.Height)
        {
            rejection = "map-footprint";
            contact = next;
            return false;
        }

        // Void cells: only cells whose unit square can overlap the circle are inspected.
        // A cell blocks when the closest point of its square lies within the radius.
        var minX = Math.Max(0, (int)MathF.Floor(next.X - radius));
        var maxX = Math.Min(map.Width - 1, (int)MathF.Ceiling(next.X + radius));
        var minY = Math.Max(0, (int)MathF.Floor(next.Y - radius));
        var maxY = Math.Min(map.Height - 1, (int)MathF.Ceiling(next.Y + radius));
        var radiusSq = radius * radius;
        for (var cy = minY; cy <= maxY; cy++)
        for (var cx = minX; cx <= maxX; cx++)
        {
            if (map.CellAt(new Vector2(cx, cy)) != 0) continue;
            var closest = new Vector2(Math.Clamp(next.X, cx, cx + 1f), Math.Clamp(next.Y, cy, cy + 1f));
            if (Vector2.DistanceSquared(next, closest) <= radiusSq)
            {
                rejection = "void-footprint";
                contact = closest;
                return false;
            }
        }

        // Occupancy: the circle must not overlap an occupant's unchanged source diamond.
        foreach (var occupant in occupants)
        {
            if (occupant.DistanceTo(next) <= radius)
            {
                rejection = $"actor:{occupant.Name}";
                contact = next;
                return false;
            }
        }

        rejection = string.Empty;
        contact = Vector2.Zero;
        return true;
    }
}

public readonly record struct MovementResult(bool Moved, Vector2 Step, string? Rejection, int Cell, int Row, bool Mirror, int SourceDirection)
{
    public static MovementResult Stopped => new(false, Vector2.Zero, null, 0, 0, false, 0);
    public static MovementResult Rejected(string reason) => new(false, Vector2.Zero, reason, 0, 0, false, 0);
    public static MovementResult Accepted(Vector2 step, int cell, int row, bool mirror, int sourceDirection) => new(true, step, null, cell, row, mirror, sourceDirection);
}
