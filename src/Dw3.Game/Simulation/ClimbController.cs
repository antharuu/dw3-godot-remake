using System.Numerics;
using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// Describes the actor's current phase while traversing a climbable wall.
/// </summary>
public enum ClimbSubstate
{
    None,
    GetOnWall,
    GetOverEdge,
    Climb,
    ClimbUp,
    ClimbDown,
    ClimbOffTop,
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
    /// Gets a value indicating whether gets whether the actor is currently committed to a wall traverse.
    /// </summary>
    public bool IsActive => _substate != ClimbSubstate.None;

    /// <summary>
    /// Gets the direction captured when the actor entered the wall traverse.
    /// </summary>
    public int Direction => _direction;

    /// <summary>
    /// Supplies the animation sequences used by future climb actions.
    /// </summary>
    public void SetData(ClimbData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>
    /// Starts an upward traverse from the foot of a wall.
    /// </summary>
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
    /// Starts a downward traverse from the top edge of a wall.
    /// </summary>
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
    public void Tick(GameState state, InputState input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        if (!IsActive) throw new InvalidOperationException("Cannot tick an inactive climb controller.");

        ApplyMovementInput(input);
        AdvanceSubstate(state);
        _animationTicks++;
        WriteTo(state);
    }

    private void InitialiseWall(int direction, int height)
    {
        _direction = direction;
        _climbSide = direction != 5 ? 1 : 0;
        _wallHeight = height * SourceUnitsPerPixel;
    }

    private void ApplyMovementInput(InputState input)
    {
        if (_substate is not (ClimbSubstate.Climb or ClimbSubstate.ClimbUp or ClimbSubstate.ClimbDown)) return;

        ClimbSubstate wanted = input.HeldUp
            ? ClimbSubstate.ClimbUp
            : input.HeldDown
                ? ClimbSubstate.ClimbDown
                : ClimbSubstate.Climb;
        TransitionTo(wanted);
    }

    private void AdvanceSubstate(GameState state)
    {
        switch (_substate)
        {
            case ClimbSubstate.GetOnWall:
                if (IsAnimationDone()) TransitionTo(ClimbSubstate.Climb);
                break;

            case ClimbSubstate.GetOverEdge:
                if (!_overEdgeOffsetApplied)
                {
                    state.Position += new Vector2(SideOffset(), EdgeClearancePixels + (_wallHeight / SourceUnitsPerPixel));
                    state.CameraSmoothingTicks = CameraSmoothingHoldTicks;
                    _overEdgeOffsetApplied = true;
                }
                if (IsAnimationDone()) TransitionTo(ClimbSubstate.Climb);
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
                if (IsAnimationDone()) FinishAtTop(state);
                break;

            case ClimbSubstate.ClimbOffBottom:
                if (IsAnimationDone()) FinishAtBottom(state);
                break;
        }
    }

    private void FinishAtTop(GameState state)
    {
        state.Position += new Vector2(-SideOffset(), -(EdgeClearancePixels + (_wallHeight / SourceUnitsPerPixel)));
        state.CameraSmoothingTicks = CameraSmoothingHoldTicks;
        EndClimb(state);
    }

    private void FinishAtBottom(GameState state)
    {
        state.SetFacing(0);
        EndClimb(state);
    }

    private void EndClimb(GameState state)
    {
        _substate = ClimbSubstate.None;
        _climbHeight = 0;
        state.RestoreFieldControl();
    }

    private void TransitionTo(ClimbSubstate next)
    {
        if (_substate == next) return;
        _substate = next;
        _animationSet = AnimationSetFor(next);
        _animationTicks = 0;
    }

    private bool IsAnimationDone()
    {
        if (_data is null || !_data.Sequences.TryGetValue(_animationSet, out ClimbSequence? sequence)) return false;
        if (sequence.Loops || sequence.Records.Count == 0) return false;

        int totalDuration = sequence.Records.Sum(record => record.Duration);
        return totalDuration <= _animationTicks;
    }

    private int SideOffset() => _climbSide != 0 ? SideShiftPixels : -SideShiftPixels;

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

    private void WriteTo(GameState state)
    {
        state.ClimbActive = IsActive;
        state.ClimbHeight = _climbHeight;
        state.ClimbSide = _climbSide;
        state.ClimbAnimationSet = _animationSet;
        state.ClimbAnimationTicks = _animationTicks;
        state.AnimationDirection = _direction;
    }

    private void EnsureData()
    {
        if (_data is null) throw new InvalidOperationException("Climb animation data must be supplied before starting a climb.");
    }
}
