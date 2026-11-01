using Xunit;

namespace Dw3.Game.Tests;

using System.Numerics;
using Dw3Production.Conversion;
using Dw3Production.Simulation;

public sealed class LegacyClimbConsoleTests
{

    [Fact]
    public void PreservesLegacyConsoleAssertions()
    {
        ClimbUpReachesTopAndRestoresControl();
        ClimbDownReachesFootAndRestoresControl();
        HeldStillGripsTheWallWithoutMoving();
        ClimbAnimationSamplesSourceCadenceAndHoldsLastFrame();
    }

    // startClimbUp places the actor at the wall foot (x,y), climbHeight 0, climbSide 1
    // for dir != 5, and wallHeight = height << 8. Held Up climbs to the top, then
    // CLIMB_OFF_TOP restores control and hops onto the ledge: pos.x +- 16, pos.y - (24+height).
    private static void ClimbUpReachesTopAndRestoresControl()
    {
        const int height = 32;
        var state = new GameState { Position = Vector2.Zero, Facing = 3 };
        var climb = new ClimbController();
        climb.SetData(Data());
        climb.StartClimbUp(state, dir: 3, x: 100, y: 100, height);
        Check(state.ClimbActive && state.Position == new Vector2(100, 100) && state.ClimbHeight == 0 && state.ClimbSide == 1,
            "climb up starts at the wall foot with zero height and climbSide 1");
        var input = new InputState();
        input.SetDirections(left: false, right: false, up: true, down: false);
        var maxHeight = 0;
        for (var tick = 0; tick < 200 && state.ClimbActive; tick++)
        {
            climb.Tick(state, input);
            maxHeight = Math.Max(maxHeight, state.ClimbHeight);
        }
        Check(!state.ClimbActive, "climb up ends and restores control");
        Check(maxHeight == height << 8, "climbHeight reached exactly the source wall height");
        Check(state.Position == new Vector2(84, 44),
            $"climb off top lands on the ledge (side=1 -> x-16, y-(24+{height})) actual={state.Position}");
    }

    // startClimbDown places the actor at the wall foot, climbHeight = wallHeight = height<<8.
    // GET_OVER_EDGE hops to the wall top (pos.x +- 16, pos.y + 24+height), then held Down
    // descends to the foot and CLIMB_OFF_BOTTOM restores control with dir 0.
    private static void ClimbDownReachesFootAndRestoresControl()
    {
        const int height = 32;
        var state = new GameState { Position = Vector2.Zero, Facing = 3 };
        var climb = new ClimbController();
        climb.SetData(Data());
        climb.StartClimbDown(state, dir: 3, x: 100, y: 100, height);
        Check(state.ClimbActive && state.ClimbHeight == height << 8 && state.ClimbSide == 1,
            "climb down starts at full wall height");
        var input = new InputState();
        input.SetDirections(left: false, right: false, up: false, down: true);
        var minHeight = int.MaxValue;
        for (var tick = 0; tick < 200 && state.ClimbActive; tick++)
        {
            climb.Tick(state, input);
            minHeight = Math.Min(minHeight, state.ClimbHeight);
        }
        Check(!state.ClimbActive, "climb down ends and restores control");
        Check(minHeight == 0, "climbHeight reached the wall foot");
        Check(state.Facing == 0, "climb off bottom resets the source direction to 0");
        Check(state.Position == new Vector2(116, 156),
            $"get-over-edge hopped onto the wall top (side=1 -> x+16, y+24+{height}) actual={state.Position}");
    }

    // Held still (no Up/Down) grips the wall: the CLIMB substate keeps climbHeight fixed.
    private static void HeldStillGripsTheWallWithoutMoving()
    {
        var state = new GameState { Position = Vector2.Zero, Facing = 3 };
        var climb = new ClimbController();
        climb.SetData(Data());
        climb.StartClimbUp(state, dir: 3, x: 100, y: 100, height: 32);
        var input = new InputState();
        // Run through GET_ON_WALL to CLIMB, then hold still.
        for (var tick = 0; tick < 20; tick++) climb.Tick(state, input);
        Check(state.ClimbActive && state.ClimbHeight == 0, "get-on-wall led to a gripped climb at zero height");
        var before = state.ClimbHeight;
        for (var tick = 0; tick < 10; tick++) climb.Tick(state, input);
        Check(state.ClimbHeight == before && state.ClimbActive, "held still keeps the climb height constant");
    }

    private static void ClimbAnimationSamplesSourceCadenceAndHoldsLastFrame()
    {
        var data = Data();
        // CLIMB_UP loops: 2 records (dur 6, 6) -> total 12, ticks 8 lands on the second.
        var loop = ClimbAnimation.Sample(data, 0x1A, ticks: 8);
        Check(loop.Frame == 1 && loop.OffsetX == -21, "looping climb sequence cycles by source duration");
        var wrapped = ClimbAnimation.Sample(data, 0x1A, ticks: 12);
        Check(wrapped.Frame == 0, "looping climb sequence wraps at the total duration");
        // GET_ON_WALL ends: 2 records (dur 4, 4) -> total 8; past ticks hold the last frame.
        var ended = ClimbAnimation.Sample(data, 0x1C, ticks: 100);
        Check(ended.Frame == 1, "one-shot climb sequence holds its last frame after it ends");
    }

    private static ClimbData Data()
    {
        var sequences = new Dictionary<int, ClimbSequence>
        {
            [0x1A] = new(0x1A, true, [new ClimbRecord(0, 6, -21, -32), new ClimbRecord(1, 6, -21, -32)]),
            [0x1B] = new(0x1B, true, [new ClimbRecord(1, 6, -21, -32)]),
            [0x1C] = new(0x1C, false, [new ClimbRecord(0, 4, -17, -29), new ClimbRecord(1, 4, -17, -29)]),
            [0x1D] = new(0x1D, false, [new ClimbRecord(0, 4, -21, -32)]),
            [0x1E] = new(0x1E, false, [new ClimbRecord(0, 4, -25, -49)]),
            [0x1F] = new(0x1F, false, [new ClimbRecord(0, 4, -20, -32)]),
            [0x20] = new(0x20, true, [new ClimbRecord(0, 6, -21, -32)]),
        };
        return new ClimbData(sequences);
    }

    private static void Check(bool condition, string name)
    {
        Assert.True(condition, name);
    }
}
