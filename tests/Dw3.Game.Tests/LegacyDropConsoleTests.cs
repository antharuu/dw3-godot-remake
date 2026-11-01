using Xunit;

namespace Dw3.Game.Tests;

using System.Numerics;
using Dw3Production.Conversion;
using Dw3Production.Simulation;

public sealed class LegacyDropConsoleTests
{

    [Fact]
    public void PreservesLegacyConsoleAssertions()
    {
        DropsFromCurrentPositionAndRestoresControl();
        UsesRightSideOnlyForSourceDirectionSeven();
    }

    private static void DropsFromCurrentPositionAndRestoresControl()
    {
        const int height = 96;
        var state = new GameState { Position = new Vector2(100, 200), Facing = 3 };
        var drop = new DropController();
        drop.SetData(Data());
        drop.StartDrop(state, dir: 3, height);
        Check(state.ClimbActive && state.ClimbHeight == height << 8 && state.Position == new Vector2(84, 296),
            "drop entry applies left side shift and ground descent without snapping to slot");
        var input = new InputState();
        var sawFarFall = false;
        for (var tick = 0; tick < 100 && state.ClimbActive; tick++)
        {
            drop.Tick(state, input);
            sawFarFall |= state.ClimbAnimationSet == 0x18;
        }
        Check(sawFarFall, "drop switches to FALL_FAR after more than 24 pixels");
        Check(!state.ClimbActive && state.ClimbHeight == 0 && state.AnimationSet == 1,
            "drop completes in STAND with climb state cleared");
        Check(state.Position == new Vector2(84, 296), "drop final position keeps the one-time entry shift");
    }

    private static void UsesRightSideOnlyForSourceDirectionSeven()
    {
        var state = new GameState { Position = new Vector2(100, 200), Facing = 7 };
        var drop = new DropController();
        drop.SetData(Data());
        drop.StartDrop(state, dir: 7, height: 32);
        Check(state.ClimbSide == 1 && state.Position == new Vector2(116, 232),
            "only source direction 7 shifts right on drop entry");
    }

    private static ClimbData Data() => new(new Dictionary<int, ClimbSequence>
    {
        [0x16] = new(0x16, false, [new ClimbRecord(0, 2, -16, -32)]),
        [0x17] = new(0x17, true, [new ClimbRecord(1, 2, -16, -32)]),
        [0x18] = new(0x18, true, [new ClimbRecord(2, 2, -16, -32)]),
        [0x19] = new(0x19, false, [new ClimbRecord(3, 2, -16, -32)]),
    });

    private static void Check(bool condition, string name)
    {
        Assert.True(condition, name);
    }
}
