using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

public enum DropSubstate
{
    None,
    DropOff,
    Fall,
    Land,
}

/// <summary>
/// Source-shaped SLOT_DROP action. It shares the climb presentation fields because the
/// source uses climbHeight as the sprite's vertical offset while the ground position moves
/// to the lower landing immediately on entry.
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

    public bool IsActive => _substate != DropSubstate.None;

    public void SetData(ClimbData data) => _data = data;

    public void StartDrop(GameState state, int dir, int height)
    {
        if (_data is null) throw new InvalidOperationException("drop data unavailable before drop start");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (dir is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(dir));
        _direction = dir;
        _climbSide = dir != 7 ? 0 : 1;
        _wallHeight = height << 8;
        _climbHeight = _wallHeight;
        // runDrop step 0. Apply in entry to avoid rendering one pre-drop frame.
        state.Position = new System.Numerics.Vector2(
            state.Position.X + (_climbSide != 0 ? SideShiftPixels : -SideShiftPixels),
            state.Position.Y + height);
        state.SetFacing(dir);
        state.AnimationDirection = dir;
        state.Offer = false;
        Transition(DropSubstate.DropOff);
        WriteTo(state);
    }

    public void Tick(GameState state, InputState input)
    {
        if (!IsActive) throw new InvalidOperationException("drop tick without an active drop");
        switch (_substate)
        {
            case DropSubstate.DropOff:
                if (IsAnimDone) Transition(DropSubstate.Fall);
                break;
            case DropSubstate.Fall:
                _climbHeight -= FallStep;
                if (_wallHeight - _climbHeight > FarFallThreshold) _animSet = AnimFallFar;
                if (_climbHeight <= 0)
                {
                    _climbHeight = 0;
                    Transition(DropSubstate.Land);
                }
                break;
            case DropSubstate.Land:
                if (IsAnimDone) EndDrop(state);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        _animTicks++;
        WriteTo(state);
    }

    private void EndDrop(GameState state)
    {
        _substate = DropSubstate.None;
        _climbHeight = 0;
        state.RestoreFieldControl();
    }

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

    private bool IsAnimDone
    {
        get
        {
            if (_data is null || !_data.Sequences.TryGetValue(_animSet, out var sequence) || sequence.Loops) return false;
            var total = sequence.Records.Sum(record => record.Duration);
            return _animTicks >= total;
        }
    }

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
