using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// The cliff drop triggered by a drop slot of the stage. It shares the climb presentation fields
/// because, as in the original, the climb height is the sprite's vertical offset while the ground
/// position moves to the lower landing immediately on entry. The actor falls 3 pixels per tick
/// and switches to the long-fall animation after 24 pixels.
/// </summary>
public sealed class DropController
{
    private const int FallStep = 0x300;
    private const int FarFallThreshold = 0x1800;
    private const int SideShiftPixels = 16;
    private const int AnimDropOff = 0x16;
    private const int AnimFall = 0x17;
    private const int AnimFallFar = 0x18;
    private const int AnimLand = 0x19;

    private ClimbData? _data;
    private DropSubstate _substate;
    private int _climbHeight;
    private int _wallHeight;
    private int _climbSide;
    private int _direction;
    private int _animSet;
    private int _animTicks;

    /// <summary>Gets a value indicating whether a drop holds the actor. While true, field movement is ignored and only <see cref="Tick"/> moves it.</summary>
    public bool IsActive => _substate != DropSubstate.None;

    /// <summary>Supplies the drop animation sets 0x16..0x19 (drop off, fall, long fall, land) from the action archive.</summary>
    /// <param name="data">Drop animation sets keyed by set id.</param>
    public void SetData(ClimbData data) => _data = data;

    /// <summary>Starts a drop from a ledge. The ground position moves at once to the landing,
    /// 16 pixels sideways and <paramref name="height"/> pixels down, so no pre-drop frame is drawn,
    /// and the sprite is lifted back by the full height before it falls.</summary>
    /// <param name="state">Game state that receives the position, facing and climb fields.</param>
    /// <param name="dir">Facing direction of the drop slot, 0..7. Direction 7 shifts right, every other direction shifts left.</param>
    /// <param name="height">Drop height in pixels, 1 or more.</param>
    /// <exception cref="InvalidOperationException">No drop data was supplied.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is 0 or negative, or <paramref name="dir"/> is outside 0..7.</exception>
    public void StartDrop(GameState state, int dir, int height)
    {
        if (_data is null)
        {
            throw new InvalidOperationException("drop data unavailable before drop start");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (dir is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(dir));
        }

        _direction = dir;
        _climbSide = dir != 7 ? 0 : 1;
        _wallHeight = height << 8;
        _climbHeight = _wallHeight;
        // First step of the drop, applied on entry to avoid rendering one pre-drop frame.
        state.Position = new System.Numerics.Vector2(
            state.Position.X + (_climbSide != 0 ? SideShiftPixels : -SideShiftPixels),
            state.Position.Y + height);
        state.SetFacing(dir);
        state.AnimationDirection = dir;
        state.Offer = false;
        Transition(DropSubstate.DropOff);
        WriteTo(state);
    }

    /// <summary>Advances the drop by one simulation tick: drop off animation, fall, then landing animation before control returns. Input is ignored during the whole drop.</summary>
    /// <param name="state">Game state updated with the climb fields and released to field control on landing.</param>
    /// <param name="input">Unused, kept so all field actions share one tick signature.</param>
    /// <exception cref="InvalidOperationException">No drop is active.</exception>
    public void Tick(GameState state, InputState input)
    {
        _ = input;
        if (!IsActive)
        {
            throw new InvalidOperationException("drop tick without an active drop");
        }

        switch (_substate)
        {
            case DropSubstate.DropOff:
                if (IsAnimDone)
                {
                    Transition(DropSubstate.Fall);
                }

                break;
            case DropSubstate.Fall:
                _climbHeight -= FallStep;
                if (_wallHeight - _climbHeight > FarFallThreshold)
                {
                    _animSet = AnimFallFar;
                }

                if (_climbHeight <= 0)
                {
                    _climbHeight = 0;
                    Transition(DropSubstate.Land);
                }

                break;
            case DropSubstate.Land:
                if (IsAnimDone)
                {
                    EndDrop(state);
                }

                break;
            default:
                throw new InvalidOperationException($"Unhandled {nameof(DropSubstate)}: {_substate}");
        }

        _animTicks++;
        WriteTo(state);
    }

    /// <summary>Clears the drop and hands the actor back to normal field movement.</summary>
    /// <param name="state">Game state restored to field control.</param>
    private void EndDrop(GameState state)
    {
        _substate = DropSubstate.None;
        _climbHeight = 0;
        state.RestoreFieldControl();
    }

    /// <summary>Enters a drop phase and restarts its animation set from tick 0.</summary>
    /// <param name="next">Phase to enter, never <see cref="DropSubstate.None"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="next"/> has no animation set.</exception>
    private void Transition(DropSubstate next)
    {
        _substate = next;
        _animSet = next switch
        {
            DropSubstate.DropOff => AnimDropOff,
            DropSubstate.Fall => AnimFall,
            DropSubstate.Land => AnimLand,
            _ => throw new ArgumentOutOfRangeException(nameof(next)),
        };
        _animTicks = 0;
    }

    /// <summary>Gets a value indicating whether the current one-shot animation has played all its records. Always false for looping or missing sets.</summary>
    private bool IsAnimDone
    {
        get
        {
            if (_data is null || !_data.Sequences.TryGetValue(_animSet, out var sequence) || sequence.Loops)
            {
                return false;
            }

            var total = sequence.Records.Sum(record => record.Duration);
            return _animTicks >= total;
        }
    }

    /// <summary>Publishes the climb fields to the game state so the renderer draws the falling sprite at its height offset.</summary>
    /// <param name="state">Game state that receives the fields.</param>
    private void WriteTo(GameState state)
    {
        state.ClimbActive = IsActive;
        state.ClimbHeight = _climbHeight;
        state.ClimbSide = _climbSide;
        state.ClimbAnimationSet = _animSet;
        state.ClimbAnimationTicks = _animTicks;
        state.AnimationDirection = _direction;
    }
}
