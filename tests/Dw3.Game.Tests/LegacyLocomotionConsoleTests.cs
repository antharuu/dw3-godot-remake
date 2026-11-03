using System.Numerics;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class LegacyLocomotionConsoleTests
{
    [Fact]
    public void PreservesLegacyConsoleAssertions()
    {
        MovesAtSourceVectorAndStops();
        AppliesFloorMirror();
        RejectsAllCollisionPaths();
        RejectsTunnellingAtSourceScale();
        SlidesAlongContactWithoutCrossingOrJitter();
        SlidesAlongStaircaseWallWithoutCatching();
        DiagonalWallFrontalContactStaysBlocked();
        RoundedShapeMatchesSourceExtentAndRemovesCornerCatch();
        FixedClockIsRateIndependentAndBounded();
        SamplesSourceCadenceAndMirrorsSprite();
    }

    private static void MovesAtSourceVectorAndStops()
    {
        var state = NewState(new Vector2(8, 8));
        state.IsRunning = true;
        var map = Map(32, 32, 1);
        var result = Locomotion.Tick(state, SemanticDirection.Right, map, []);
        Check(
            result.Moved && Math.Abs(result.Step.X - Locomotion.SourceRunWorldUnitsPerSimulationTick) < 0.0001f && result.Step.Y == 0f,
            "flat right step preserves the derived PAL source speed");
        Check(
            Math.Abs(state.Position.X - (8f + Locomotion.SourceRunWorldUnitsPerSimulationTick)) < 0.0001f && Math.Abs(state.Position.Y - 8f) < 0.0001f,
            "position advances by the converted source step each simulation tick");
        var stoppedAt = state.Position;
        result = Locomotion.Tick(state, SemanticDirection.None, map, []);
        Check(!result.Moved && state.Position == stoppedAt && state.AnimationSet == 1, "release stops without a step");

        state = NewState(new Vector2(16, 16));
        result = Locomotion.Tick(state, SemanticDirection.UpRight, map, []);
        Check(result.Moved && result.Step.X > 0 && result.Step.Y < 0, "diagonal has both source vector components");
    }

    private static void AppliesFloorMirror()
    {
        var state = NewState(new Vector2(16, 16));
        state.IsRunning = true;
        var map = Map(32, 32, 0x11);
        var result = Locomotion.Tick(state, SemanticDirection.UpRight, map, []);
        Check(result.Moved && result.Mirror && result.SourceDirection == 5, "mirror flag records the rebased source direction");
        Check(result.Step.X > 0 && result.Step.Y < 0, "mirror negates the source x component after remap");
    }

    private static void RejectsAllCollisionPaths()
    {
        var state = Running(new Vector2(16, 16));
        Check(Locomotion.Tick(state, SemanticDirection.Right, Map(48, 48, 1), []).Moved, "open floor moves through source footprint");
        Check(Locomotion.Tick(Running(new Vector2(16, 16)), SemanticDirection.Right, Map(48, 48, 8), []).Rejection == "unclassified-floor:8", "unclassified floor rejects with raw value");
        var footprint = Enumerable.Repeat((byte)1, 32 * 32).ToArray();
        footprint[(12 * 32) + 20] = 0;
        Check(Locomotion.Tick(Running(new Vector2(12, 12)), SemanticDirection.Right, new FloorMap(32, 32, footprint), []).Rejection == "void-footprint", "rounded footprint rejects raw zero ahead");
        Check(Locomotion.Tick(Running(new Vector2(12, 12)), SemanticDirection.Right, Map(32, 32, 1), [new Occupant("gatekeeper", new Vector2(20, 12), 8)]).Rejection == "actor:gatekeeper", "rounded footprint blocks by actor name");
        Check(Locomotion.Tick(Running(new Vector2(31, 16)), SemanticDirection.Right, Map(32, 32, 1), []).Rejection == "map-footprint", "swept source footprint blocks map edge before a run can leave bounds");
    }

    private static void RejectsTunnellingAtSourceScale()
    {
        var cells = Enumerable.Repeat((byte)1, 64 * 64).ToArray();

        // The source-facing right probes reach this cell during the first bounded
        // substep, while a destination-only check would jump beyond it.
        cells[(10 * 64) + 25] = 0;
        var blocked = Locomotion.Tick(Running(new Vector2(16, 16)), SemanticDirection.Right, new FloorMap(64, 64, cells), []);
        Check(!blocked.Moved && blocked.Rejection == "void-footprint", "source-scale run sweeps intermediate wall footprints without tunnelling");

        var actorBlocked = Locomotion.Tick(Running(new Vector2(16, 16)), SemanticDirection.Right, Map(64, 64, 1), [new Occupant("swept-actor", new Vector2(26, 10), 4)]);
        Check(!actorBlocked.Moved && actorBlocked.Rejection == "actor:swept-actor", "source-scale run sweeps intermediate actor footprints without tunnelling");
    }

    private static void SlidesAlongContactWithoutCrossingOrJitter()
    {
        var cells = Enumerable.Repeat((byte)1, 64 * 64).ToArray();

        // A vertical void wall blocks the horizontal component of an up-right source
        // step while leaving its tangential upward component open.
        for (var y = 0; y < 64; y++)
        {
            cells[(y * 64) + 26] = 0;
        }

        var map = new FloorMap(64, 64, cells);
        var state = Running(new Vector2(16, 32));
        var before = state.Position;
        var slide = Locomotion.Tick(state, SemanticDirection.UpRight, map, []);
        Check(
            slide.Moved && Math.Abs(slide.Step.X) < 0.0001f && slide.Step.Y < 0f && Math.Abs(state.Position.X - before.X) < 0.0001f && state.Position.Y < before.Y,
            "diagonal contact projects onto the free wall tangent without backing away");

        var inward = Locomotion.Tick(state, SemanticDirection.Right, map, []);
        Check(
            !inward.Moved && inward.Rejection == "void-footprint" && Math.Abs(state.Position.X - before.X) < 0.0001f,
            "pure inward input remains blocked at the contacted wall");

        // A second wall turns the tangent into a corner. Repeated diagonal input must
        // settle instead of alternating partial components or crossing either barrier.
        for (var x = 0; x < 64; x++)
        {
            cells[(20 * 64) + x] = 0;
        }

        map = new FloorMap(64, 64, cells);
        state.Position = new Vector2(16, 28);
        var last = state.Position;
        for (var tick = 0; tick < 4; tick++)
        {
            var corner = Locomotion.Tick(state, SemanticDirection.UpRight, map, []);
            Check(!corner.Moved && state.Position == last, $"corner contact settles without jitter tick={tick}");
        }

        Check(state.Position.X < 26f, "contact projection never crosses the void wall");
    }

    private static void SlidesAlongStaircaseWallWithoutCatching()
    {
        const int N = 128;

        // Diagonal staircase wall at grid slope 1/2 (void where 2y >= x + 16): free
        // above-left, void below-right. Its boundary descends down-right at the same
        // slope as the isometric DownLeft/UpRight movement axis, so a diagonal wall is
        // quantized to a staircase of single cells. Under the M1 cardinal projection
        // alone this exact held DownLeft route stalls on 10 of 20 ticks as the wide
        // source leading-edge probes catch each protruding cell.
        var cells = Enumerable.Repeat((byte)1, N * N).ToArray();
        for (var y = 0; y < N; y++)
        {
            for (var x = 0; x < N; x++)
            {
                if (2 * y >= x + 16)
                {
                    cells[(y * N) + x] = 0;
                }
            }
        }

        var map = new FloorMap(N, N, cells);
        var state = Running(new Vector2(100, 20));
        var start = state.Position;
        var rejections = 0;
        for (var tick = 0; tick < 20; tick++)
        {
            var result = Locomotion.Tick(state, SemanticDirection.DownLeft, map, []);
            Check(map.CellAt(state.Position) != 0, $"staircase slide never enters void tick={tick}");
            if (result.Moved)
            {
                Check(result.Step.Length() <= Locomotion.SourceRunWorldUnitsPerSimulationTick + 1e-3f, $"staircase slide never exceeds the source step tick={tick}");
            }
            else
            {
                rejections++;
            }
        }

        Check(rejections == 0, $"staircase slide glides without catching (rejections={rejections})");
        Check(Vector2.Distance(state.Position, start) > 30f, "staircase slide keeps making progress along the wall");
    }

    private static void RoundedShapeMatchesSourceExtentAndRemovesCornerCatch()
    {
        // The M2 comfort circle must keep the source cardinal half-extent exactly, and
        // must remove the diamond's convex-corner catch: a single void cell on the old
        // corner probe (+8,+8) landed inside the removed diamond but stays at distance
        // sqrt(8^2+8^2) > 8 from the circle centre, so a diagonal step glides past.
        Check(
            Math.Abs(Locomotion.PlayerCollisionRadius - 8f) < 0.0001f,
            "rounded collision radius equals the +-8 source cardinal half-extent");

        const int N = 40;
        var cells = Enumerable.Repeat((byte)1, N * N).ToArray();
        cells[(18 * N) + 18] = 0; // void exactly on the (8,8) corner of a player at (10,10)
        var map = new FloorMap(N, N, cells);

        // Reference: the removed diamond sampled the corner probe at (+8,+8) for a
        // down-right step, landing in the void cell. The circle keeps that cell at
        // distance sqrt(128) from the centre, so the same step is free.
        var removedDiamondCornerProbe = new Vector2(10 + 8, 10 + 8);
        Check(map.CellAt(removedDiamondCornerProbe) == 0, "removed diamond corner probe sits in the void cell");

        var state = Running(new Vector2(10, 10));
        var result = Locomotion.Tick(state, SemanticDirection.DownRight, map, []);
        Check(
            result.Moved && result.Rejection is null,
            $"rounded circle glides past the old diamond corner without catching (rejection={result.Rejection})");

        // The same circle must still hard-block a frontal push into a real void cell at
        // its cardinal reach: place the centre eight units left of a void column and push
        // straight into it.
        var wall = Enumerable.Repeat((byte)1, N * N).ToArray();
        for (var y = 0; y < N; y++)
        {
            wall[(y * N) + 26] = 0;
        }

        var wallMap = new FloorMap(N, N, wall);
        var frontal = Running(new Vector2(18, 20));
        var blocked = Locomotion.Tick(frontal, SemanticDirection.Right, wallMap, []);
        Check(
            !blocked.Moved && blocked.Rejection == "void-footprint",
            "rounded circle still hard-blocks a frontal cardinal push into a wall");
    }

    private static void DiagonalWallFrontalContactStaysBlocked()
    {
        const int N = 128;
        var cells = Enumerable.Repeat((byte)1, N * N).ToArray();
        for (var y = 0; y < N; y++)
        {
            for (var x = 0; x < N; x++)
            {
                if (2 * y >= x + 16)
                {
                    cells[(y * N) + x] = 0;
                }
            }
        }

        var map = new FloorMap(N, N, cells);

        // A push straight into the diagonal wall (down-right, perpendicular to its
        // smoothed contour) has no tangential component, so it stays blocked and never
        // crosses the boundary, exactly like a frontal contact on a flat wall.
        foreach (var dir in new[] { SemanticDirection.DownRight, SemanticDirection.Down })
        {
            var state = Running(new Vector2(30, 20));
            var result = Locomotion.Tick(state, dir, map, []);
            Check(!result.Moved && result.Rejection == "void-footprint", $"diagonal wall frontal contact {dir} stays blocked");
            Check(map.CellAt(state.Position) != 0, $"diagonal wall frontal contact {dir} never crosses void");
        }
    }

    private static void FixedClockIsRateIndependentAndBounded()
    {
        var positions = new List<Vector2>();
        foreach (var renderHz in new[] { 30, 60, 144 })
        {
            var clock = new SimClock();
            var state = Running(new Vector2(100, 100));
            var map = Map(1024, 1024, 1);
            var frames = renderHz * 2;
            for (var frame = 0; frame < frames; frame++)
            {
                var ticks = clock.Tick(1.0 / renderHz);
                for (var tick = 0; tick < ticks; tick++)
                {
                    Locomotion.Tick(state, SemanticDirection.Right, map, []);
                }
            }

            positions.Add(state.Position);
        }

        var xs = positions.Select(pos => pos.X).ToArray();
        Check(xs.Max() - xs.Min() < 1.5, "30/60/144 Hz render cadence produces consistent distance");
        Check(
            Math.Abs(xs.Average() - (100 + (Locomotion.SourceRunWorldUnitsPerSecond * 2))) < 1.5,
            "two seconds covers the derived PAL run distance at fixed 60 Hz");
        var stalled = new SimClock();
        Check(stalled.Tick(1) == SimClock.MaxTicksPerFrame && stalled.LastDroppedTicks == 56, "one-second stall is capped and excess debt discarded");
        Check(stalled.Tick(SimClock.StepSeconds) == 1 && stalled.LastDroppedTicks == 0, "stall cannot create later catch-up burst");
    }

    private static void SamplesSourceCadenceAndMirrorsSprite()
    {
        var player = new PlayerData(
        [
            new PlayerSequence(4, 2, [new AnimationRecord(10, 2, -16, -28, 0, 1), new AnimationRecord(11, 3, -15, -27, 1, 2)]),
            new PlayerSequence(4, 1, [new AnimationRecord(12, 5, -16, -28, 2, 3)]),
            new PlayerSequence(5, 2, [new AnimationRecord(20, 1, -16, -28, 0, 1)]),
        ],
        -16,
        -28);
        var state = NewState(Vector2.Zero);
        state.AnimationSet = 4;
        state.AnimationDirection = 2;
        state.AnimationTicks = 2;
        var sample = PlayerAnimation.Sample(player, state);
        Check(sample.Frame == 11 && sample.OffsetX == -15 && sample.FootGroundY == 2, "animation selects frame by source duration and applies offset/contact");
        state.AnimationDirection = 6;
        state.AnimationTicks = 0;
        sample = PlayerAnimation.Sample(player, state);
        Check(sample.SourceDirection == 2 && sample.FlipH, "left-facing semantic direction mirrors available source sprite sequence");
        state.IsRunning = true;
        state.Position = new Vector2(16, 16);
        var run = Locomotion.Tick(state, SemanticDirection.Right, Map(64, 64, 1), []);
        Check(
            run.Moved && Math.Abs(run.Step.X - 3.9973958f) < 0.0001f && run.Step.Y == 0f && state.AnimationSet == 5,
            "default source run scales PAL 0x4CC actor speed to the fixed 60 Hz simulation");
        state.IsRunning = false;
        var walk = Locomotion.Tick(state, SemanticDirection.Right, Map(64, 64, 1), []);
        Check(
            walk.Moved && Math.Abs(walk.Step.X - 0.99934896f) < 0.0001f && walk.Step.Y == 0f && state.AnimationSet == 4,
            "explicit local walk modifier retains the source WALK/run quarter ratio");
    }

    private static GameState NewState(Vector2 position) => new() { Position = position, Facing = (int)SemanticDirection.Right, AnimationSet = 1, AnimationDirection = (int)SemanticDirection.Right };

    private static GameState Running(Vector2 position) => new() { Position = position, Facing = (int)SemanticDirection.Right, AnimationSet = 1, AnimationDirection = (int)SemanticDirection.Right, IsRunning = true };

    private static FloorMap Map(int width, int height, byte cell) => new(width, height, [.. Enumerable.Repeat(cell, width * height)]);

    private static void Check(bool condition, string name)
    {
        Assert.True(condition, name);
    }
}
