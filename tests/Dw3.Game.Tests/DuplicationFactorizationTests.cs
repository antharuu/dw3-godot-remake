using Xunit;
using Dw3Production.Simulation;

namespace Dw3.Game.Tests;

/// <summary>
/// Protects the five duplicate-family factorisations required by K4.
///
/// Families covered here (engine-free, no Godot types):
///   F1 — InputState.DirectionFrom: a single static helper replaces the duplicate eight-way
///        tuple switch present in both InputState.Direction and GameHost.DirectionToward.
///   F2 — GameState.RestoreFieldControl: a single method replaces the six-line reset sequence
///        shared by ClimbController.EndClimb and DropController.EndDrop.
///
/// Families F3 (image-region count loops), F4 (map-image dimension validators), and
/// F5 (capture-root setup) involve Godot Image / Rect2 / filesystem types that require the
/// Godot runtime.  Those families are protected by the existing DW3_DIALOGUE_PROBE headless
/// run, which exercises all three code paths and whose pass is recorded in EVIDENCE.
/// </summary>
public sealed class DuplicationFactorizationTests
{
    // -------------------------------------------------------------------------
    // F1 — InputState.DirectionFrom
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(true, false, false, false, SemanticDirection.Up)]
    [InlineData(true, false, false, true, SemanticDirection.UpRight)]
    [InlineData(false, false, false, true, SemanticDirection.Right)]
    [InlineData(false, true, false, true, SemanticDirection.DownRight)]
    [InlineData(false, true, false, false, SemanticDirection.Down)]
    [InlineData(false, true, true, false, SemanticDirection.DownLeft)]
    [InlineData(false, false, true, false, SemanticDirection.Left)]
    [InlineData(true, false, true, false, SemanticDirection.UpLeft)]
    [InlineData(false, false, false, false, SemanticDirection.None)]
    public void DirectionFrom_MapsAllNineInputsCorrectly(
        bool up, bool down, bool left, bool right, SemanticDirection expected)
    {
        var actual = InputState.DirectionFrom(up, down, left, right);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InputState_Direction_MatchesDirectionFrom()
    {
        // The Direction property must delegate to DirectionFrom rather than repeating the switch.
        // Verify all nine cases via SetDirections to confirm no drift with DirectionFrom.
        var state = new InputState();

        state.SetDirections(left: false, right: false, up: true, down: false);
        Assert.Equal(SemanticDirection.Up, state.Direction);
        Assert.Equal(InputState.DirectionFrom(up: true, down: false, left: false, right: false), state.Direction);

        state.SetDirections(left: false, right: true, up: true, down: false);
        Assert.Equal(SemanticDirection.UpRight, state.Direction);
        Assert.Equal(InputState.DirectionFrom(up: true, down: false, left: false, right: true), state.Direction);

        state.SetDirections(left: false, right: true, up: false, down: false);
        Assert.Equal(SemanticDirection.Right, state.Direction);
        Assert.Equal(InputState.DirectionFrom(up: false, down: false, left: false, right: true), state.Direction);

        state.SetDirections(left: false, right: true, up: false, down: true);
        Assert.Equal(SemanticDirection.DownRight, state.Direction);
        Assert.Equal(InputState.DirectionFrom(up: false, down: true, left: false, right: true), state.Direction);

        state.SetDirections(left: false, right: false, up: false, down: true);
        Assert.Equal(SemanticDirection.Down, state.Direction);

        state.SetDirections(left: true, right: false, up: false, down: true);
        Assert.Equal(SemanticDirection.DownLeft, state.Direction);

        state.SetDirections(left: true, right: false, up: false, down: false);
        Assert.Equal(SemanticDirection.Left, state.Direction);

        state.SetDirections(left: true, right: false, up: true, down: false);
        Assert.Equal(SemanticDirection.UpLeft, state.Direction);

        state.SetDirections(left: false, right: false, up: false, down: false);
        Assert.Equal(SemanticDirection.None, state.Direction);
    }

    // -------------------------------------------------------------------------
    // F2 — GameState.RestoreFieldControl
    // -------------------------------------------------------------------------

    [Fact]
    public void RestoreFieldControl_ClearsClimbAndAnimation()
    {
        var state = new GameState
        {
            Facing = 3,
            ClimbActive = true,
            ClimbHeight = 512,
            AnimationSet = 5,
            AnimationDirection = 7,
            AnimationTicks = 42,
        };

        state.RestoreFieldControl();

        Assert.False(state.ClimbActive, "ClimbActive must be cleared");
        Assert.Equal(0, state.ClimbHeight);
        Assert.Equal(1, state.AnimationSet);
        Assert.Equal(3, state.AnimationDirection); // must equal Facing
        Assert.Equal(0, state.AnimationTicks);
    }

    [Fact]
    public void RestoreFieldControl_AnimationDirectionTracksFacing()
    {
        foreach (var facing in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
        {
            var state = new GameState
            {
                Facing = facing,
                ClimbActive = true,
                ClimbHeight = 100,
                AnimationSet = 9,
                AnimationDirection = 0,
                AnimationTicks = 99,
            };
            state.RestoreFieldControl();
            Assert.Equal(facing, state.AnimationDirection);
        }
    }
}
