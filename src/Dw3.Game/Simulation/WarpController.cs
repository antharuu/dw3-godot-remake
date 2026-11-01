using System.Numerics;
using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

public enum WarpState
{
    Idle,
    Offer,
    LockInput,
    ValidateTarget,
    Fade,
    Commit,
    Release,
    Rearm,
}

public sealed class TriggerMap
{
    private readonly byte[] _cells;

    public TriggerMap(int width, int height, byte[] cells)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (cells.Length != checked(width * height)) throw new ArgumentException("trigger cell count does not match dimensions", nameof(cells));
        Width = width;
        Height = height;
        _cells = cells.ToArray();
    }

    public int Width { get; }
    public int Height { get; }

    public byte CellAt(Vector2 position)
    {
        var x = Math.Clamp((int)MathF.Round(position.X), 0, Width - 1);
        var y = Math.Clamp((int)MathF.Round(position.Y), 0, Height - 1);
        return _cells[y * Width + x];
    }

    public IEnumerable<(Vector2 Position, byte Raw)> CellsWithin(Vector2 position, float radius)
    {
        var minX = Math.Max(0, (int)MathF.Ceiling(position.X - radius));
        var maxX = Math.Min(Width - 1, (int)MathF.Floor(position.X + radius));
        var minY = Math.Max(0, (int)MathF.Ceiling(position.Y - radius));
        var maxY = Math.Min(Height - 1, (int)MathF.Floor(position.Y + radius));
        for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var candidate = new Vector2(x, y);
                if (Vector2.Distance(position, candidate) <= radius) yield return (candidate, _cells[y * Width + x]);
            }
    }
}

public sealed class AreaMap
{
    private readonly byte[] _cells;

    public AreaMap(int width, int height, byte[] cells)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (cells.Length != checked(width * height)) throw new ArgumentException("area cell count does not match dimensions", nameof(cells));
        Width = width;
        Height = height;
        _cells = cells.ToArray();
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>The battle area (1..4) at the actor's tile, or 0 (no area). Mirrors
    /// FIELDSTG_getMapCell(FIELD_MAP_AREAS): the fixed-point tile is truncated with &gt;&gt; 8.</summary>
    public byte CellAt(Vector2 position)
    {
        var x = Math.Clamp((int)position.X, 0, Width - 1);
        var y = Math.Clamp((int)position.Y, 0, Height - 1);
        return _cells[y * Width + x];
    }
}

public sealed record FieldZone(Zone Zone, FloorMap Floor, TriggerMap Triggers, IReadOnlyList<Occupant> Occupants, AreaMap? Areas = null);

/// <summary>
/// Owns the only legal warp path: Idle -> Offer -> LockInput -> ValidateTarget ->
/// Fade -> Commit -> Release -> Rearm -> Idle. Presentation observes the state but
/// cannot commit a destination itself.
/// </summary>
public sealed class WarpController
{
    // Semantic reconstruction of FIELDSTG_nearDirs: raw trigger direction and
    // player-facing are source directions (0=down, counter-clockwise). An exit
    // offers only when facing is within 45 degrees of its encoded direction.
    private static readonly bool[,] SourceNearDirections =
    {
        { true, true, false, false, false, false, false, true },
        { true, true, true, false, false, false, false, false },
        { false, true, true, true, false, false, false, false },
        { false, false, true, true, true, false, false, false },
        { false, false, false, true, true, true, false, false },
        { false, false, false, false, true, true, true, false },
        { false, false, false, false, false, true, true, true },
        { true, false, false, false, false, false, true, true },
    };

    // Intentional M1 comfort adaptation: accept a source exit cell within two world
    // units when the short direct segment stays on non-void source floor.
    public const float OfferRadiusWorldUnits = 2f;

    private const int FadeTicks = 2;
    private Exit? _pendingExit;
    private Climb? _pendingClimb;
    private Drop? _pendingDrop;
    private int _pendingClimbDirection = -1;
    private FieldZone? _pendingTarget;
    private int _fadeRemaining;
    private string? _reportedUnimplemented;
    private readonly HashSet<string> _reportedExitConditions = new();

    public WarpState State { get; private set; } = WarpState.Idle;
    public bool InputLocked => State is WarpState.LockInput or WarpState.ValidateTarget or WarpState.Fade or WarpState.Commit;
    public int CommitCount { get; private set; }

    public static bool IsSourceFacingAdmitted(int triggerDirection, int actorFacing) =>
        triggerDirection is >= 0 and <= 7 && actorFacing is >= 0 and <= 7 && SourceNearDirections[triggerDirection, actorFacing];

    /// <summary>
    /// Advances the field-transition state machine. Target loading completes before
    /// <paramref name="applyScene"/> commits the new field; traversal callbacks are dispatched only
    /// from admitted offers, so callers must not re-enter this controller from those callbacks.
    /// </summary>
    public void Tick(
        GameState state,
        InputState input,
        FieldZone current,
        Func<string, FieldZone> loadTarget,
        Action<FieldZone> applyScene,
        Action<string> info,
        Action<string, string> warn,
        Action<Climb, int> startClimb,
        Action<Drop, int>? startDrop = null)
    {
        var actionPressed = input.ConsumeActionPressed();
        switch (State)
        {
            case WarpState.Idle:
                if (TryOffer(state, current, out var exit, out var climb, out var drop, out var climbDirection))
                {
                    _pendingExit = exit;
                    _pendingClimb = climb;
                    _pendingDrop = drop;
                    _pendingClimbDirection = climbDirection;
                    state.Offer = true;
                    if (exit is not null) ReportUnevaluatedExitCondition(exit, current, warn);
                    Transition(WarpState.Offer, info);
                }
                else
                {
                    ReportUnimplementedExit(state, current, warn);
                    state.Offer = false;
                }
                break;

            case WarpState.Offer:
                if (!TryOffer(state, current, out var offeredExit, out var offeredClimb, out var offeredDrop, out var offeredClimbDirection))
                {
                    _pendingExit = null;
                    _pendingClimb = null;
                    _pendingDrop = null;
                    _pendingClimbDirection = -1;
                    state.Offer = false;
                    Transition(WarpState.Idle, info);
                    break;
                }
                _pendingExit = offeredExit;
                _pendingClimb = offeredClimb;
                _pendingDrop = offeredDrop;
                _pendingClimbDirection = offeredClimbDirection;
                state.Offer = true;
                if (offeredExit is not null) ReportUnevaluatedExitCondition(offeredExit, current, warn);
                if (actionPressed && !input.ActionHeld)
                    throw new InvalidOperationException("action edge cannot be released before warp admission");
                if (!actionPressed) break;
                if (_pendingClimb is not null)
                {
                    // SLOT_CLIMB_UP/DOWN: set off the climb instead of leaving the field.
                    var climbSlot = _pendingClimb;
                    var climbDir = _pendingClimbDirection;
                    _pendingClimb = null;
                    _pendingDrop = null;
                    _pendingClimbDirection = -1;
                    state.Offer = false;
                    startClimb(climbSlot, climbDir);
                    Transition(WarpState.Idle, info);
                }
                else if (_pendingDrop is not null)
                {
                    var dropSlot = _pendingDrop;
                    var dropDir = _pendingClimbDirection;
                    _pendingDrop = null;
                    _pendingClimbDirection = -1;
                    state.Offer = false;
                    (startDrop ?? throw new InvalidOperationException("drop dispatch callback unavailable"))(dropSlot, dropDir);
                    Transition(WarpState.Idle, info);
                }
                else
                {
                    Transition(WarpState.LockInput, info);
                }
                break;

            case WarpState.LockInput:
                Transition(WarpState.ValidateTarget, info);
                break;

            case WarpState.ValidateTarget:
                ValidateTarget(current, loadTarget, info, warn);
                break;

            case WarpState.Fade:
                if (--_fadeRemaining <= 0) Transition(WarpState.Commit, info);
                break;

            case WarpState.Commit:
                Commit(state, applyScene, info);
                break;

            case WarpState.Release:
                _pendingExit = null;
                _pendingTarget = null;
                Transition(WarpState.Rearm, info);
                break;

            case WarpState.Rearm:
                state.Offer = false;
                if (!input.ActionHeld) Transition(WarpState.Idle, info);
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ReportUnimplementedExit(GameState state, FieldZone current, Action<string, string> warn)
    {
        var raw = current.Triggers.CellAt(state.Position);
        var slot = raw & 0x1F;
        if (raw == 0 || current.Zone.Exits.Any(exit => exit.Slot == slot) || current.Zone.Climbs.Any(climb => climb.Slot == slot) || (current.Zone.Drops?.Any(drop => drop.Slot == slot) ?? false))
        {
            _reportedUnimplemented = null;
            return;
        }
        var key = $"{current.Zone.Id}:{slot}";
        if (_reportedUnimplemented == key) return;
        _reportedUnimplemented = key;
        warn($"suppressed-dispatch:{key}", $"suppressed-dispatch zone={current.Zone.Id} slot={slot} reason=exit-unimplemented");
    }

    /// <summary>
    /// Signals a story-gated physical edge when it is first offered. The edge is
    /// crossable for the exploration phase, but its raw condition is still carried
    /// and the offer is logged once per zone:slot so the non-enforcement is explicit
    /// rather than silent.
    /// </summary>
    private void ReportUnevaluatedExitCondition(Exit exit, FieldZone current, Action<string, string> warn)
    {
        if (!exit.IsUnevaluated) return;
        var key = $"{current.Zone.Id}:{exit.Slot}";
        if (!_reportedExitConditions.Add(key)) return;
        warn($"exit-condition-not-enforced:{key}", $"exit-condition-not-enforced zone={current.Zone.Id} slot={exit.Slot} condition=0x{exit.Condition!.Value:X4}");
    }

    private bool TryOffer(GameState state, FieldZone current, out Exit? exit, out Climb? climb, out Drop? drop, out int climbDirection)
    {
        exit = null;
        climb = null;
        drop = null;
        climbDirection = -1;
        foreach (var candidate in current.Triggers.CellsWithin(state.Position, OfferRadiusWorldUnits)
                     .Where(candidate => candidate.Raw != 0)
                     .OrderBy(candidate => Vector2.DistanceSquared(state.Position, candidate.Position)))
        {
            var slot = candidate.Raw & 0x1F;
            var triggerDirection = candidate.Raw >> 5;
            if (!IsSourceFacingAdmitted(triggerDirection, state.Facing)) continue;
            if (!FloorPathOpen(state.Position, candidate.Position, current.Floor)) continue;
            var matchedExit = current.Zone.Exits.FirstOrDefault(item => item.Slot == slot);
            if (matchedExit is not null)
            {
                exit = matchedExit;
                state.OfferedTriggerDirection = triggerDirection;
                return true;
            }
            var matchedClimb = current.Zone.Climbs.FirstOrDefault(item => item.Slot == slot);
            if (matchedClimb is not null)
            {
                climb = matchedClimb;
                climbDirection = triggerDirection;
                state.OfferedTriggerDirection = triggerDirection;
                return true;
            }
            var matchedDrop = current.Zone.Drops?.FirstOrDefault(item => item.Slot == slot);
            if (matchedDrop is not null)
            {
                drop = matchedDrop;
                climbDirection = triggerDirection;
                state.OfferedTriggerDirection = triggerDirection;
                return true;
            }
        }
        state.OfferedTriggerDirection = -1;
        return false;
    }

    private static bool FloorPathOpen(Vector2 start, Vector2 end, FloorMap floor)
    {
        var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(start, end)));
        for (var index = 0; index <= steps; index++)
        {
            var point = Vector2.Lerp(start, end, index / (float)steps);
            if (!floor.Contains(point) || floor.CellAt(point) == 0) return false;
        }
        return true;
    }

    private void ValidateTarget(FieldZone current, Func<string, FieldZone> loadTarget, Action<string> info, Action<string, string> warn)
    {
        if (_pendingExit is null) throw new InvalidOperationException("warp validation without pending exit");
        try
        {
            var target = loadTarget(_pendingExit.Destination);
            if (!target.Zone.Exits.Any(exit => exit.Destination == current.Zone.Id))
                throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"zone={_pendingExit.Destination} reciprocal exit unavailable");
            _pendingTarget = target;
            _fadeRemaining = FadeTicks;
            Transition(WarpState.Fade, info);
        }
        catch (CacheResourceException error)
        {
            warn("warp-cache-unavailable", $"CACHE_RESOURCE_UNAVAILABLE zone={_pendingExit.Destination} detail={error.Message}");
            _pendingExit = null;
            _pendingTarget = null;
            Transition(WarpState.Release, info);
        }
    }

    private void Commit(GameState state, Action<FieldZone> applyScene, Action<string> info)
    {
        if (_pendingExit is null || _pendingTarget is null) throw new InvalidOperationException("warp commit without a validated target");
        state.ZoneId = _pendingExit.Destination;
        state.Position = new Vector2(_pendingExit.X, _pendingExit.Y);
        state.SetFacing(_pendingExit.Facing);
        state.AnimationDirection = state.Facing;
        state.AnimationSet = 1;
        state.AnimationTicks = 0;
        state.Offer = false;
        CommitCount++;
        applyScene(_pendingTarget);
        info($"warp commit={CommitCount} zone={state.ZoneId} position=({state.Position.X:0},{state.Position.Y:0}) facing={state.Facing}");
        Transition(WarpState.Release, info);
    }

    private void Transition(WarpState next, Action<string> info)
    {
        info($"warp state={State}->{next}");
        State = next;
    }
}
