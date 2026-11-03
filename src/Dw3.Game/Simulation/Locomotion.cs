using System.Numerics;

namespace Dw3Production.Simulation;

/// <summary>
/// The player's movement on the field: maps a SemanticDirection input to a displacement
/// in world coordinates using the disc's per-slope direction-vector table (floor cell
/// slope rows 0..6 of the stage map), at the original 50 Hz PAL speed scaled to the 60 Hz simulation tick.
/// The player's collision footprint is a circle of fixed radius replacing the original
/// octagon (intentional adaptation: natural wall-slide with no corner stalls).
/// Engine-free; tested against fabricated floor maps.
/// </summary>
public static class Locomotion
{
    // The EU version moves the player at 0x4CC/256 = 4.796875 world units per PAL task
    // update at 50 Hz. The core simulates at 60 Hz, hence the 50/60 conversion.

    /// <summary>Run speed in world units per 60 Hz simulation tick (EU disc rate × 50/60), about 4 units.</summary>
    public const float SourceRunWorldUnitsPerSimulationTick = (0x4CC / 256f) * (50f / 60f);

    /// <summary>Walk speed: one quarter of the run speed, matching the original walk divisor.</summary>
    public const float SourceWalkWorldUnitsPerSimulationTick = SourceRunWorldUnitsPerSimulationTick / 4f;

    /// <summary>Run speed in world units per second (60 Hz tick rate × 60).</summary>
    public const float SourceRunWorldUnitsPerSecond = SourceRunWorldUnitsPerSimulationTick * 60f;

    // Comfort adaptation (documented, not original behavior): the original footprint is a
    // discrete diamond of five direction-specific leading-edge probes (offsets +-8/+-6).
    // Its convex corners catch on protruding void
    // cells, so a held diagonal still stalls against a staircase wall despite the tangent
    // slide. This replaces the diamond with a circle of equal cardinal half-extent (+-8).
    // A circle has no corner, so a wall contact always yields a natural tangent. It is
    // inscribed in the former square half-extent, so it never reaches beyond the original
    // +-8 cardinal reach and no obstacle is weakened.

    /// <summary>Player collision radius in world units (comfort adaptation: a circle equal in cardinal reach to the original ±8 diamond).</summary>
    public const float PlayerCollisionRadius = 8f;

    // The removed original diamond's octagon outline (world offsets from the actor centre),
    // retained only for the F3 comparison rendering. The active shape is the circle above.

    /// <summary>
    /// Original diamond outline as eight world-offset vertices from the actor centre, retained
    /// for the F3 diagnostic overlay. The active collision shape is the circle above.
    /// </summary>
    public static readonly Vector2[] SourceDiamondOutline =
    [
        new(-6, -8), new(6, -8), new(8, -6), new(8, 6), new(6, 8), new(-6, 8), new(-8, 6), new(-8, -6),
    ];

    private static readonly int[] MirrorDirections = [0, 7, 6, 5, 4, 3, 2, 1];

    // Per-slope, per-direction displacement vectors (8.8 fixed-point, 4096 = 1.0 world unit).
    // Row 0 = flat floor; rows 1..6 = the stage map's slope entries; columns = disc direction 0..7.
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
    /// <param name="state">Mutable game state; facing and animation are always written, position only on acceptance.</param>
    /// <param name="input">Semantic movement direction from the input system.</param>
    /// <param name="map">The floor map of the current zone.</param>
    /// <param name="occupants">Actors whose diamond footprints block the player.</param>
    /// <returns>A <see cref="MovementResult"/> describing the outcome of the step.</returns>
    public static MovementResult Tick(GameState state, SemanticDirection input, FloorMap map, IReadOnlyList<Occupant> occupants)
    {
        if (input == SemanticDirection.None)
        {
            state.AnimationSet = 1;
            state.AnimationDirection = state.Facing;
            return MovementResult.Stopped;
        }

        var direction = (int)input;
        // SemanticDirection is 0=up clockwise; the disc's direction vectors, facing and
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
        if (row < 0 || row > 6)
        {
            return MovementResult.Rejected($"unclassified-floor:{cell}");
        }

        var mirror = (cell & 0x10) != 0;
        var effective = mirror ? MirrorDirections[sourceDirection] : sourceDirection;
        var vector = DirectionVectors[row, effective];
        // The original walk step uses the complete 8.8 actor speed for RUN and
        // one quarter for WALK. Convert its 50 Hz PAL task cadence to the fixed 60 Hz tick.
        var scale = state.IsRunning ? SourceRunWorldUnitsPerSimulationTick : SourceWalkWorldUnitsPerSimulationTick;
        var step = new Vector2(vector.X * scale * (mirror ? -1 : 1) / 4096f, vector.Y * scale / 4096f);
        var meta = new FloorMeta(cell, row, mirror, sourceDirection);
        if (!FootprintFreeAcrossStep(state.Position, step, map, occupants, out var rejection, out var contact))
        {
            return TrySlide(state, step, map, occupants, rejection, contact, meta);
        }

        state.Position += step;
        state.AnimationSet = state.IsRunning ? 5 : 4;
        return MovementResult.Accepted(step, meta.Cell, meta.Row, meta.Mirror, meta.SourceDirection);
    }

    // Radius (in floor cells) over which the void contour is smoothed to derive the
    // local wall surface normal. Comparable to the original leading-edge probe reach
    // (+-8 cells), so a staircase boundary is sampled at the actor's own scale rather
    // than per protruding cell.
    private const int SurfaceSmoothRadius = 6;

    /// <summary>
    /// Groups the floor metadata that travels through the slide helpers and lands in
    /// <see cref="MovementResult"/>, so a slid step reports the floor it started from.
    /// </summary>
    /// <param name="Cell">Raw floor cell byte under the actor.</param>
    /// <param name="Row">Classified slope row of the direction-vector table, 0..6.</param>
    /// <param name="Mirror">Whether the slope row is used mirrored.</param>
    /// <param name="SourceDirection">Input direction rebased for the slope row, used to look up the direction vector.</param>
    private readonly record struct FloorMeta(int Cell, int Row, bool Mirror, int SourceDirection);

    /// <summary>
    /// Tries to keep the actor moving after its full step was rejected: first along the smoothed
    /// wall tangent when a void cell blocked it, then along the longer, then the shorter, of the
    /// step's two axis components.
    /// </summary>
    /// <param name="state">Player state, moved and set to the walk or run animation on success.</param>
    /// <param name="step">The rejected step, in map pixels.</param>
    /// <param name="map">Floor map of the zone.</param>
    /// <param name="occupants">Actors the footprint must not overlap.</param>
    /// <param name="rejection">Why the full step was rejected, for example <c>void-footprint</c>.</param>
    /// <param name="contact">First blocked point found along the step.</param>
    /// <param name="meta">Floor metadata of the starting position.</param>
    /// <returns>An accepted result with the step actually taken, or a rejection with the original reason when no slide fits.</returns>
    private static MovementResult TrySlide(
        GameState state,
        Vector2 step,
        FloorMap map,
        IReadOnlyList<Occupant> occupants,
        string rejection,
        Vector2 contact,
        FloorMeta meta)
    {
        if (rejection == "void-footprint")
        {
            var tangent = TryTangentSlide(state, step, map, occupants, contact, meta);
            if (tangent is not null)
            {
                return tangent.Value;
            }
        }

        // Intentional comfort adaptation: project the rejected step onto one signed
        // cardinal component. This preserves obstacles, source scale and swept probes.
        var candidates = new[] { new Vector2(step.X, 0), new Vector2(0, step.Y) }
            .Where(c => c != Vector2.Zero)
            .OrderByDescending(c => c.LengthSquared());
        foreach (var candidate in candidates)
        {
            if (!FootprintFreeAcrossStep(state.Position, candidate, map, occupants, out _, out _))
            {
                continue;
            }

            state.Position += candidate;
            state.AnimationSet = state.IsRunning ? 5 : 4;
            return MovementResult.Accepted(candidate, meta.Cell, meta.Row, meta.Mirror, meta.SourceDirection);
        }

        return MovementResult.Rejected(rejection);
    }

    /// <summary>
    /// Comfort adaptation (documented, not original behavior): before the
    /// cardinal projection, projects the rejected step onto the local smoothed
    /// wall tangent so a diagonal or irregular (staircase) void contour glides
    /// instead of catching on its convex corners.
    /// </summary>
    /// <remarks>
    /// The tangent is derived from the disc's void cells at the first swept contact point only;
    /// the swept probes and occupancy remain authoritative, so no obstacle is
    /// weakened or crossed and the slide never exceeds the input step magnitude
    /// (no hidden acceleration). Concave corners keep their wall-normal slide,
    /// which the opposite wall then blocks, so a corner stays blocked exactly
    /// like a frontal contact.
    /// </remarks>
    /// <param name="state">Player state, moved on success.</param>
    /// <param name="step">The rejected step, in map pixels.</param>
    /// <param name="map">Floor map of the zone.</param>
    /// <param name="occupants">Actors the footprint must not overlap.</param>
    /// <param name="contact">First blocked point, where the wall normal is sampled.</param>
    /// <param name="meta">Floor metadata of the starting position.</param>
    /// <returns>The accepted slide, or null when there is no wall normal, the step does not push into the wall, or the tangent step is blocked too.</returns>
    private static MovementResult? TryTangentSlide(
        GameState state,
        Vector2 step,
        FloorMap map,
        IReadOnlyList<Occupant> occupants,
        Vector2 contact,
        FloorMeta meta)
    {
        var slideNormal = ComputeSurfaceNormal(map, contact);
        var intoWall = slideNormal == Vector2.Zero ? 0f : Vector2.Dot(step, slideNormal);
        if (intoWall >= 0f)
        {
            return null;
        }

        var tangentStep = step - slideNormal * intoWall;
        if (tangentStep == Vector2.Zero)
        {
            return null;
        }

        if (!FootprintFreeAcrossStep(state.Position, tangentStep, map, occupants, out _, out _))
        {
            return null;
        }

        state.Position += tangentStep;
        state.AnimationSet = state.IsRunning ? 5 : 4;
        return MovementResult.Accepted(tangentStep, meta.Cell, meta.Row, meta.Mirror, meta.SourceDirection);
    }

    /// <summary>
    /// Computes the outward (away-from-void) unit normal of the smoothed void contour
    /// around a position. The normal is the negated
    /// gradient of a box-smoothed blocked density, so it follows a wall's average
    /// orientation instead of individual staircase cells. Every value is derived from
    /// the real floor cells of the disc; nothing is invented.
    /// </summary>
    /// <param name="map">Floor map of the zone.</param>
    /// <param name="position">Point to sample, in map pixels.</param>
    /// <returns>A unit vector pointing away from the void, or <see cref="Vector2.Zero"/> when the density is flat (no void in reach, or void on all sides).</returns>
    private static Vector2 ComputeSurfaceNormal(FloorMap map, Vector2 position)
    {
        var cx = (int)position.X;
        var cy = (int)position.Y;
        var gx = ComputeBlockedDensity(map, cx + SurfaceSmoothRadius, cy) - ComputeBlockedDensity(map, cx - SurfaceSmoothRadius, cy);
        var gy = ComputeBlockedDensity(map, cx, cy + SurfaceSmoothRadius) - ComputeBlockedDensity(map, cx, cy - SurfaceSmoothRadius);
        var gradient = new Vector2(gx, gy);
        return gradient == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(-gradient);
    }

    /// <summary>
    /// Fraction of map cells blocked within a 13 × 13 box (twice the smoothing radius plus one) centred on (bx, by).
    /// Out-of-bounds probes count as blocked. Used to smooth the void contour into a wall normal.
    /// </summary>
    /// <param name="map">Floor map of the zone.</param>
    /// <param name="bx">Box centre X, in cells.</param>
    /// <param name="by">Box centre Y, in cells.</param>
    /// <returns>The blocked fraction, 0..1.</returns>
    private static float ComputeBlockedDensity(FloorMap map, int bx, int by)
    {
        var blocked = 0;
        var total = 0;
        for (var dy = -SurfaceSmoothRadius; dy <= SurfaceSmoothRadius; dy++)
        {
            for (var dx = -SurfaceSmoothRadius; dx <= SurfaceSmoothRadius; dx++)
            {
                total++;
                var probe = new Vector2(bx + dx, by + dy);
                if (!map.Contains(probe) || map.CellAt(probe) == 0)
                {
                    blocked++;
                }
            }
        }

        return blocked / (float)total;
    }

    /// <summary>Checks the footprint at evenly spaced points along a step, at most one pixel apart, so a fast step cannot tunnel through a thin wall or an actor.</summary>
    /// <param name="start">Starting position, in map pixels.</param>
    /// <param name="step">Displacement to check.</param>
    /// <param name="map">Floor map of the zone.</param>
    /// <param name="occupants">Actors the footprint must not overlap.</param>
    /// <param name="rejection">Receives <c>map-bound</c>, <c>map-footprint</c>, <c>void-footprint</c> or <c>actor:&lt;name&gt;</c> on failure, empty on success.</param>
    /// <param name="contact">Receives the first blocked point on failure, zero on success.</param>
    /// <returns><see langword="true"/> when every intermediate footprint is free.</returns>
    private static bool FootprintFreeAcrossStep(Vector2 start, Vector2 step, FloorMap map, IReadOnlyList<Occupant> occupants, out string rejection, out Vector2 contact)
    {
        // An original PAL run step is nearly five tiles. The 60 Hz conversion remains nearly
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

            if (!FootprintFree(next, map, occupants, out rejection, out contact))
            {
                return false;
            }
        }

        rejection = string.Empty;
        contact = Vector2.Zero;
        return true;
    }

    /// <summary>Checks the circular player footprint at one position against the map bounds, the void cells and the occupants.</summary>
    /// <param name="next">Footprint centre, in map pixels.</param>
    /// <param name="map">Floor map of the zone.</param>
    /// <param name="occupants">Actors the footprint must not overlap.</param>
    /// <param name="rejection">Receives the reason on failure, empty on success.</param>
    /// <param name="contact">Receives the blocked point on failure, zero on success.</param>
    /// <returns><see langword="true"/> when the circle fits.</returns>
    private static bool FootprintFree(Vector2 next, FloorMap map, IReadOnlyList<Occupant> occupants, out string rejection, out Vector2 contact)
    {
        // The original footprint is a discrete diamond of five
        // direction-specific leading-edge probes. Its convex corners catch on protruding
        // void cells, so even with the tangent slide a held diagonal stalls against a
        // staircase wall. The comfort adaptation replaces it with a circle of equal
        // cardinal half-extent (PlayerCollisionRadius), which has no corner: a wall
        // contact always yields a natural tangent. The circle is inscribed in the former
        // square half-extent, so it never reaches beyond the +-8 original cardinal reach.
        // Obstacles (raw void and out-of-bounds) and occupancy (the unchanged original
        // diamond) stay authoritative, and the swept substeps prevent tunnelling.
        var radius = PlayerCollisionRadius;

        // The circle must stay entirely inside the floor bounds.
        if (next.X - radius < 0f || next.Y - radius < 0f
            || next.X + radius > map.Width || next.Y + radius > map.Height)
        {
            rejection = "map-footprint";
            contact = next;
            return false;
        }

        if (!VoidCellsFree(next, radius, map, out rejection, out contact))
        {
            return false;
        }

        return CheckOccupancy(next, radius, occupants, out rejection, out contact);
    }

    /// <summary>Checks that no void cell (floor byte 0) overlaps the footprint circle.</summary>
    /// <param name="next">Circle centre, in map pixels.</param>
    /// <param name="radius">Circle radius, in pixels.</param>
    /// <param name="map">Floor map of the zone.</param>
    /// <param name="rejection">Receives <c>void-footprint</c> on failure, empty on success.</param>
    /// <param name="contact">Receives the closest point of the first overlapping cell, zero on success.</param>
    /// <returns><see langword="true"/> when no void cell is within the radius.</returns>
    private static bool VoidCellsFree(Vector2 next, float radius, FloorMap map, out string rejection, out Vector2 contact)
    {
        // Void cells: only cells whose unit square can overlap the circle are inspected.
        // A cell blocks when the closest point of its square lies within the radius.
        var minX = Math.Max(0, (int)MathF.Floor(next.X - radius));
        var maxX = Math.Min(map.Width - 1, (int)MathF.Ceiling(next.X + radius));
        var minY = Math.Max(0, (int)MathF.Floor(next.Y - radius));
        var maxY = Math.Min(map.Height - 1, (int)MathF.Ceiling(next.Y + radius));
        var radiusSq = radius * radius;
        for (var cy = minY; cy <= maxY; cy++)
        {
            for (var cx = minX; cx <= maxX; cx++)
            {
                if (map.CellAt(new Vector2(cx, cy)) != 0)
                {
                    continue;
                }

                var closest = new Vector2(Math.Clamp(next.X, cx, cx + 1f), Math.Clamp(next.Y, cy, cy + 1f));
                if (Vector2.DistanceSquared(next, closest) <= radiusSq)
                {
                    rejection = "void-footprint";
                    contact = closest;
                    return false;
                }
            }
        }

        rejection = string.Empty;
        contact = Vector2.Zero;
        return true;
    }

    /// <summary>Checks that the footprint circle does not reach any occupant's diamond footprint.</summary>
    /// <param name="position">Circle centre, in map pixels.</param>
    /// <param name="radius">Circle radius, in pixels.</param>
    /// <param name="occupants">Actors to test, in order. The first hit is reported.</param>
    /// <param name="rejection">Receives <c>actor:&lt;name&gt;</c> on failure, empty on success.</param>
    /// <param name="contact">Receives <paramref name="position"/> on failure, zero on success.</param>
    /// <returns><see langword="true"/> when no occupant is within the radius.</returns>
    private static bool CheckOccupancy(Vector2 position, float radius, IReadOnlyList<Occupant> occupants, out string rejection, out Vector2 contact)
    {
        // Occupancy: the circle must not overlap an occupant's unchanged original diamond.
        foreach (var occupant in occupants)
        {
            if (occupant.DistanceTo(position) <= radius)
            {
                rejection = $"actor:{occupant.Name}";
                contact = position;
                return false;
            }
        }

        rejection = string.Empty;
        contact = Vector2.Zero;
        return true;
    }
}
