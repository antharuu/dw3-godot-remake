using System.Numerics;
using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// Describes the actor's current phase while traversing a climbable wall.
/// </summary>
public enum ClimbSubstate
{
    /// <summary>No climb in progress. The player is under normal field control.</summary>
    None,

    /// <summary>Mounting the wall from its foot (animation set 0x1C), played once.</summary>
    GetOnWall,

    /// <summary>Stepping over the top edge onto the wall to climb down (animation set 0x1E), played once.</summary>
    GetOverEdge,

    /// <summary>Hanging on the wall with no vertical input (idle set 0x20, looping).</summary>
    Climb,

    /// <summary>Moving up one pixel per tick while up is held (set 0x1A).</summary>
    ClimbUp,

    /// <summary>Moving down one pixel per tick while down is held (set 0x1B).</summary>
    ClimbDown,

    /// <summary>Leaving the wall over its top edge (set 0x1D), played once before control returns.</summary>
    ClimbOffTop,

    /// <summary>Leaving the wall at its foot (set 0x1F), played once before control returns.</summary>
    ClimbOffBottom,
}

/// <summary>
/// Simulates mounting, traversing, and leaving a climbable wall on the fixed game clock.
/// </summary>
public sealed class ClimbController
{
    private const int SourceUnitsPerPixel = 256;

    private const int ClimbStep = SourceUnitsPerPixel;

    private const int SideShiftPixels = 16;

    private const int EdgeClearancePixels = 24;

    private const int CameraSmoothingHoldTicks = 16;

    private const int ClimbUpSet = 0x1A;

    private const int ClimbDownSet = 0x1B;

    private const int GetOnWallSet = 0x1C;

    private const int ClimbOffTopSet = 0x1D;

    private const int GetOverEdgeSet = 0x1E;

    private const int ClimbOffBottomSet = 0x1F;

    private const int ClimbIdleSet = 0x20;

    private ClimbData? _data;

    private ClimbSubstate _substate;

    private int _direction;

    private int _climbSide;

    private int _climbHeight;

    private int _wallHeight;

    private int _animationSet;

    private int _animationTicks;

    private bool _overEdgeOffsetApplied;

    /// <summary>
    /// Gets a value indicating whether the actor is committed to a wall traverse. While true, field movement is ignored and only <see cref="Tick"/> moves the actor.
    /// </summary>
    public bool IsActive => _substate != ClimbSubstate.None;

    /// <summary>
    /// Gets the direction captured when the actor entered the wall traverse, 0..7. It stays fixed for the whole climb and selects the side the actor shifts to.
    /// </summary>
    public int Direction => _direction;

    /// <summary>
    /// Supplies the animation sequences used by future climb actions.
    /// </summary>
    /// <param name="data">Climb animation sets from the pack, keyed by set id 0x1A..0x20.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    public void SetData(ClimbData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>
    /// Starts an upward traverse from the foot of a wall.
    /// </summary>
    /// <param name="state">Game state that receives the position, facing and climb fields.</param>
    /// <param name="dir">Facing direction of the climb slot, 0..7.</param>
    /// <param name="x">Foot position X in pixels.</param>
    /// <param name="y">Foot position Y in pixels.</param>
    /// <param name="height">Wall height in pixels.</param>
    /// <exception cref="InvalidOperationException">No climb data was supplied.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public void StartClimbUp(GameState state, int dir, int x, int y, int height)
    {
        EnsureData();
        ArgumentNullException.ThrowIfNull(state);

        InitialiseWall(dir, height);
        _climbHeight = 0;
        _overEdgeOffsetApplied = false;
        TransitionTo(ClimbSubstate.GetOnWall);

        state.Position = new Vector2(x, y);
        state.SetFacing(dir);
        state.AnimationDirection = dir;
        state.Offer = false;
        WriteTo(state);
    }

    /// <summary>
    /// Starts a downward traverse from the top edge of a wall. The actor is moved past the edge
    /// (16 pixels sideways, 24 pixels plus the wall height down) and the camera smoothing is held
    /// for 16 ticks so the jump does not snap the view.
    /// </summary>
    /// <param name="state">Game state that receives the position, facing and climb fields.</param>
    /// <param name="dir">Facing direction of the climb slot, 0..7.</param>
    /// <param name="x">Top edge position X in pixels.</param>
    /// <param name="y">Top edge position Y in pixels.</param>
    /// <param name="height">Wall height in pixels.</param>
    /// <exception cref="InvalidOperationException">No climb data was supplied.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public void StartClimbDown(GameState state, int dir, int x, int y, int height)
    {
        EnsureData();
        ArgumentNullException.ThrowIfNull(state);

        InitialiseWall(dir, height);
        _climbHeight = _wallHeight;
        _overEdgeOffsetApplied = true;
        TransitionTo(ClimbSubstate.GetOverEdge);

        state.Position = new Vector2(x + SideOffset(), y + EdgeClearancePixels + height);
        state.SetFacing(dir);
        state.AnimationDirection = dir;
        state.Offer = false;
        state.CameraSmoothingTicks = CameraSmoothingHoldTicks;
        WriteTo(state);
    }

    /// <summary>
    /// Advances the active wall traverse by one fixed simulation tick.
    /// </summary>
    /// <param name="state">Game state updated with the climb fields, and released back to field control when the climb ends.</param>
    /// <param name="input">Held directions: up and down choose the climb direction while on the wall.</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="input"/> is null.</exception>
    /// <exception cref="InvalidOperationException">No climb is active.</exception>
    public void Tick(GameState state, InputState input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        if (!IsActive)
        {
            throw new InvalidOperationException("Cannot tick an inactive climb controller.");
        }

        ApplyMovementInput(input);
        AdvanceSubstate(state);
        _animationTicks++;
        WriteTo(state);
    }

    /// <summary>Maps a substate to the climb animation set it plays, 0x1A..0x20 in the action archive.</summary>
    /// <param name="substate">Substate to map.</param>
    /// <returns>The set id, or 0 for <see cref="ClimbSubstate.None"/>.</returns>
    private static int AnimationSetFor(ClimbSubstate substate) => substate switch
    {
        ClimbSubstate.GetOnWall => GetOnWallSet,
        ClimbSubstate.GetOverEdge => GetOverEdgeSet,
        ClimbSubstate.Climb => ClimbIdleSet,
        ClimbSubstate.ClimbUp => ClimbUpSet,
        ClimbSubstate.ClimbDown => ClimbDownSet,
        ClimbSubstate.ClimbOffTop => ClimbOffTopSet,
        ClimbSubstate.ClimbOffBottom => ClimbOffBottomSet,
        _ => 0,
    };

    /// <summary>Stores the climb direction and wall height for a new traverse. Direction 5 climbs on the left side, every other direction on the right.</summary>
    /// <param name="direction">Facing direction of the climb slot, 0..7.</param>
    /// <param name="height">Wall height in pixels, stored internally in 1/256 pixel units.</param>
    private void InitialiseWall(int direction, int height)
    {
        _direction = direction;
        _climbSide = direction != 5 ? 1 : 0;
        _wallHeight = height * SourceUnitsPerPixel;
    }

    /// <summary>Picks climbing up, down or hanging from the held directions. Ignored while a mount or dismount animation plays, so those always finish. Up wins when both are held.</summary>
    /// <param name="input">Current held input.</param>
    private void ApplyMovementInput(InputState input)
    {
        if (_substate is not (ClimbSubstate.Climb or ClimbSubstate.ClimbUp or ClimbSubstate.ClimbDown))
        {
            return;
        }

        ClimbSubstate wanted = ClimbSubstate.Climb;
        if (input.HeldUp)
        {
            wanted = ClimbSubstate.ClimbUp;
        }
        else if (input.HeldDown)
        {
            wanted = ClimbSubstate.ClimbDown;
        }

        TransitionTo(wanted);
    }

    /// <summary>Runs one tick of the current substate: one-shot animations move on when done, climbing moves one pixel and switches to the matching dismount when the height reaches the top or the foot.</summary>
    /// <param name="state">Game state, changed only when the actor leaves the wall or steps over the edge.</param>
    private void AdvanceSubstate(GameState state)
    {
        switch (_substate)
        {
            case ClimbSubstate.GetOnWall:
                if (IsAnimationDone())
                {
                    TransitionTo(ClimbSubstate.Climb);
                }

                break;

            case ClimbSubstate.GetOverEdge:
                AdvanceGetOverEdge(state);
                break;

            case ClimbSubstate.ClimbUp:
                _climbHeight += ClimbStep;
                if (_climbHeight >= _wallHeight)
                {
                    _climbHeight = _wallHeight;
                    TransitionTo(ClimbSubstate.ClimbOffTop);
                }

                break;

            case ClimbSubstate.ClimbDown:
                _climbHeight -= ClimbStep;
                if (_climbHeight <= 0)
                {
                    _climbHeight = 0;
                    TransitionTo(ClimbSubstate.ClimbOffBottom);
                }

                break;

            case ClimbSubstate.ClimbOffTop:
                if (IsAnimationDone())
                {
                    FinishAtTop(state);
                }

                break;

            case ClimbSubstate.ClimbOffBottom:
                if (IsAnimationDone())
                {
                    FinishAtBottom(state);
                }

                break;
        }
    }

    /// <summary>On the first tick of a step over the edge that was not prepositioned, moves the actor past the edge and holds the camera smoothing. Then waits for the animation to finish before hanging on the wall.</summary>
    /// <param name="state">Game state whose position is shifted once.</param>
    private void AdvanceGetOverEdge(GameState state)
    {
        if (!_overEdgeOffsetApplied)
        {
            state.Position += new Vector2(SideOffset(), EdgeClearancePixels + (_wallHeight / SourceUnitsPerPixel));
            state.CameraSmoothingTicks = CameraSmoothingHoldTicks;
            _overEdgeOffsetApplied = true;
        }

        if (IsAnimationDone())
        {
            TransitionTo(ClimbSubstate.Climb);
        }
    }

    /// <summary>Ends a climb over the top edge: undoes the edge shift so the actor stands on the upper floor, holds the camera smoothing and returns control.</summary>
    /// <param name="state">Game state that receives the final position.</param>
    private void FinishAtTop(GameState state)
    {
        state.Position += new Vector2(-SideOffset(), -(EdgeClearancePixels + (_wallHeight / SourceUnitsPerPixel)));
        state.CameraSmoothingTicks = CameraSmoothingHoldTicks;
        EndClimb(state);
    }

    /// <summary>Ends a climb at the foot of the wall facing direction 0 (down), away from the wall, and returns control.</summary>
    /// <param name="state">Game state that receives the final facing.</param>
    private void FinishAtBottom(GameState state)
    {
        state.SetFacing(0);
        EndClimb(state);
    }

    /// <summary>Clears the climb and hands the actor back to normal field movement.</summary>
    /// <param name="state">Game state restored to field control.</param>
    private void EndClimb(GameState state)
    {
        _substate = ClimbSubstate.None;
        _climbHeight = 0;
        state.RestoreFieldControl();
    }

    /// <summary>Switches substate and restarts the matching animation set from tick 0. Staying in the same substate keeps the animation running, so a held direction does not restart its loop.</summary>
    /// <param name="next">Substate to enter.</param>
    private void TransitionTo(ClimbSubstate next)
    {
        if (_substate == next)
        {
            return;
        }

        _substate = next;
        _animationSet = AnimationSetFor(next);
        _animationTicks = 0;
    }

    /// <summary>Tells whether the current one-shot animation has played all its records.</summary>
    /// <returns><see langword="true"/> once the elapsed ticks reach the sum of the record durations. Always <see langword="false"/> for looping sets and for sets missing from the data.</returns>
    private bool IsAnimationDone()
    {
        if (_data is null || !_data.Sequences.TryGetValue(_animationSet, out ClimbSequence? sequence))
        {
            return false;
        }

        if (sequence.Loops || sequence.Records.Count == 0)
        {
            return false;
        }

        int totalDuration = sequence.Records.Sum(record => record.Duration);
        return totalDuration <= _animationTicks;
    }

    /// <summary>Sideways shift applied when stepping over or off the top edge.</summary>
    /// <returns>+16 pixels on the right side, -16 on the left side.</returns>
    private int SideOffset() => _climbSide != 0 ? SideShiftPixels : -SideShiftPixels;

    /// <summary>Publishes the climb fields to the game state so the renderer and the F3 overlay can draw the actor on the wall.</summary>
    /// <param name="state">Game state that receives the fields.</param>
    private void WriteTo(GameState state)
    {
        state.ClimbActive = IsActive;
        state.ClimbHeight = _climbHeight;
        state.ClimbSide = _climbSide;
        state.ClimbAnimationSet = _animationSet;
        state.ClimbAnimationTicks = _animationTicks;
        state.AnimationDirection = _direction;
    }

    /// <summary>Rejects a climb start before <see cref="SetData"/> was called.</summary>
    /// <exception cref="InvalidOperationException">No climb data was supplied.</exception>
    private void EnsureData()
    {
        if (_data is null)
        {
            throw new InvalidOperationException("Climb animation data must be supplied before starting a climb.");
        }
    }
}
