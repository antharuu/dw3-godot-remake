using System.Numerics;
using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// Owns the only legal warp path: Idle → Offer → LockInput → ValidateTarget →
/// Fade → Commit → Release → Rearm → Idle. Presentation observes the state but
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

    /// <summary>Warp-offer search radius in world units (M1 comfort adaptation: two tiles).</summary>
    public const float OfferRadiusWorldUnits = 2f;

    private const int FadeTicks = 2;
    private FieldExit? _pendingExit;
    private Climb? _pendingClimb;
    private Drop? _pendingDrop;
    private int _pendingClimbDirection = -1;
    private FieldZone? _pendingTarget;
    private int _fadeRemaining;
    private string? _reportedUnimplemented;
    private readonly HashSet<string> _reportedExitConditions = [];

    /// <summary>Gets the current warp state.</summary>
    public WarpState State { get; private set; } = WarpState.Idle;

    /// <summary>Gets a value indicating whether locomotion input is locked by the warp state machine.</summary>
    public bool InputLocked => State is WarpState.LockInput or WarpState.ValidateTarget or WarpState.Fade or WarpState.Commit;

    /// <summary>Gets the number of warps committed since this controller was created.</summary>
    public int CommitCount { get; private set; }

    /// <summary>
    /// Returns true when the actor is facing within 45° of a trigger's encoded direction.
    /// Reconstructed from FIELDSTG_nearDirs (both indices are source direction 0=down CCW).
    /// </summary>
    /// <param name="triggerDirection">Source direction encoded in the trigger cell bits 7..5.</param>
    /// <param name="actorFacing">Current source facing of the actor.</param>
    /// <returns>True when the facing is admitted for this trigger direction.</returns>
    public static bool IsSourceFacingAdmitted(int triggerDirection, int actorFacing) =>
        triggerDirection is >= 0 and <= 7 && actorFacing is >= 0 and <= 7 && SourceNearDirections[triggerDirection, actorFacing];

    /// <summary>
    /// Advances the field-transition state machine. Target loading completes before
    /// <paramref name="callbacks"/>.ApplyScene commits the new field; traversal callbacks are dispatched only
    /// from admitted offers, so callers must not re-enter this controller from those callbacks.
    /// </summary>
    /// <param name="state">Mutable game state, updated on facing, position and offer changes.</param>
    /// <param name="input">Current input snapshot; action edge is consumed on admission.</param>
    /// <param name="current">The field zone currently displayed.</param>
    /// <param name="callbacks">The six warp lifecycle callbacks: load, apply, log sinks and traversal dispatchers.</param>
    public void Tick(GameState state, InputState input, FieldZone current, WarpCallbacks callbacks)
    {
        var actionPressed = input.ConsumeActionPressed();
        switch (State)
        {
            case WarpState.Idle:
                TickIdle(state, current, callbacks);
                break;
            case WarpState.Offer:
                TickOffer(state, input, current, actionPressed, callbacks);
                break;
            case WarpState.LockInput:
                Transition(WarpState.ValidateTarget, callbacks.Info);
                break;
            case WarpState.ValidateTarget:
                ValidateTarget(current, callbacks);
                break;
            case WarpState.Fade:
                if (--_fadeRemaining <= 0)
                {
                    Transition(WarpState.Commit, callbacks.Info);
                }

                break;
            case WarpState.Commit:
                Commit(state, callbacks);
                break;
            case WarpState.Release:
                _pendingExit = null;
                _pendingTarget = null;
                Transition(WarpState.Rearm, callbacks.Info);
                break;
            case WarpState.Rearm:
                state.Offer = false;
                if (!input.ActionHeld)
                {
                    Transition(WarpState.Idle, callbacks.Info);
                }

                break;
            default:
                throw new InvalidOperationException($"Unhandled {nameof(WarpState)}: {State}");
        }
    }

    private void TickIdle(GameState state, FieldZone current, WarpCallbacks callbacks)
    {
        if (TryOffer(state, current, out var exit, out var climb, out var drop, out var climbDirection))
        {
            UpdatePending(exit, climb, drop, climbDirection, state);
            if (exit is not null)
            {
                ReportUnevaluatedExitCondition(exit, current, callbacks.Warn);
            }

            Transition(WarpState.Offer, callbacks.Info);
        }
        else
        {
            ReportUnimplementedExit(state, current, callbacks.Warn);
            state.Offer = false;
        }
    }

    private void TickOffer(GameState state, InputState input, FieldZone current, bool actionPressed, WarpCallbacks callbacks)
    {
        if (!TryOffer(state, current, out var offeredExit, out var offeredClimb, out var offeredDrop, out var offeredClimbDirection))
        {
            ClearPending(state);
            Transition(WarpState.Idle, callbacks.Info);
            return;
        }

        UpdatePending(offeredExit, offeredClimb, offeredDrop, offeredClimbDirection, state);
        if (offeredExit is not null)
        {
            ReportUnevaluatedExitCondition(offeredExit, current, callbacks.Warn);
        }

        if (actionPressed && !input.ActionHeld)
        {
            throw new InvalidOperationException("action edge cannot be released before warp admission");
        }

        if (!actionPressed)
        {
            return;
        }

        DispatchAdmittedOffer(state, callbacks);
    }

    private void DispatchAdmittedOffer(GameState state, WarpCallbacks callbacks)
    {
        if (_pendingClimb is not null)
        {
            var climbSlot = _pendingClimb;
            var climbDir = _pendingClimbDirection;
            _pendingClimb = null;
            _pendingDrop = null;
            _pendingClimbDirection = -1;
            state.Offer = false;
            callbacks.StartClimb(climbSlot, climbDir);
            Transition(WarpState.Idle, callbacks.Info);
        }
        else if (_pendingDrop is not null)
        {
            var dropSlot = _pendingDrop;
            var dropDir = _pendingClimbDirection;
            _pendingDrop = null;
            _pendingClimbDirection = -1;
            state.Offer = false;
            (callbacks.StartDrop ?? throw new InvalidOperationException("drop dispatch callback unavailable"))(dropSlot, dropDir);
            Transition(WarpState.Idle, callbacks.Info);
        }
        else
        {
            Transition(WarpState.LockInput, callbacks.Info);
        }
    }

    private void ClearPending(GameState state)
    {
        _pendingExit = null;
        _pendingClimb = null;
        _pendingDrop = null;
        _pendingClimbDirection = -1;
        state.Offer = false;
    }

    private void UpdatePending(FieldExit? exit, Climb? climb, Drop? drop, int climbDirection, GameState state)
    {
        _pendingExit = exit;
        _pendingClimb = climb;
        _pendingDrop = drop;
        _pendingClimbDirection = climbDirection;
        state.Offer = true;
    }

    private void ReportUnimplementedExit(GameState state, FieldZone current, Action<string, string> warn)
    {
        var raw = current.Triggers.CellAt(state.Position);
        var slot = raw & 0x1F;
        if (raw == 0
            || current.Zone.Exits.Any(exit => exit.Slot == slot)
            || current.Zone.Climbs.Any(climb => climb.Slot == slot)
            || (current.Zone.Drops?.Any(drop => drop.Slot == slot) ?? false))
        {
            _reportedUnimplemented = null;
            return;
        }

        var key = $"{current.Zone.Id}:{slot}";
        if (_reportedUnimplemented == key)
        {
            return;
        }

        _reportedUnimplemented = key;
        warn($"suppressed-dispatch:{key}", $"suppressed-dispatch zone={current.Zone.Id} slot={slot} reason=exit-unimplemented");
    }

    /// <summary>
    /// Signals a story-gated physical edge when it is first offered. The edge is
    /// crossable for the exploration phase, but its raw condition is still carried
    /// and the offer is logged once per zone:slot so the non-enforcement is explicit
    /// rather than silent.
    /// </summary>
    private void ReportUnevaluatedExitCondition(FieldExit exit, FieldZone current, Action<string, string> warn)
    {
        if (!exit.IsUnevaluated)
        {
            return;
        }

        var key = $"{current.Zone.Id}:{exit.Slot}";
        if (!_reportedExitConditions.Add(key))
        {
            return;
        }

        warn($"exit-condition-not-enforced:{key}", $"exit-condition-not-enforced zone={current.Zone.Id} slot={exit.Slot} condition=0x{exit.Condition!.Value:X4}");
    }

    private static bool TryOffer(GameState state, FieldZone current, out FieldExit? exit, out Climb? climb, out Drop? drop, out int climbDirection)
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
            if (!IsSourceFacingAdmitted(triggerDirection, state.Facing))
            {
                continue;
            }

            if (!FloorPathOpen(state.Position, candidate.Position, current.Floor))
            {
                continue;
            }

            if (MatchExit(current, slot, triggerDirection, state, ref exit))
            {
                return true;
            }

            if (MatchClimb(current, slot, triggerDirection, state, ref climb, ref climbDirection))
            {
                return true;
            }

            if (MatchDrop(current, slot, triggerDirection, state, ref drop, ref climbDirection))
            {
                return true;
            }
        }

        state.OfferedTriggerDirection = -1;
        return false;
    }

    private static bool MatchExit(FieldZone current, int slot, int triggerDirection, GameState state, ref FieldExit? exit)
    {
        var matched = current.Zone.Exits.FirstOrDefault(item => item.Slot == slot);
        if (matched is null)
        {
            return false;
        }

        exit = matched;
        state.OfferedTriggerDirection = triggerDirection;
        return true;
    }

    private static bool MatchClimb(FieldZone current, int slot, int triggerDirection, GameState state, ref Climb? climb, ref int climbDirection)
    {
        var matched = current.Zone.Climbs.FirstOrDefault(item => item.Slot == slot);
        if (matched is null)
        {
            return false;
        }

        climb = matched;
        climbDirection = triggerDirection;
        state.OfferedTriggerDirection = triggerDirection;
        return true;
    }

    private static bool MatchDrop(FieldZone current, int slot, int triggerDirection, GameState state, ref Drop? drop, ref int climbDirection)
    {
        var matched = current.Zone.Drops?.FirstOrDefault(item => item.Slot == slot);
        if (matched is null)
        {
            return false;
        }

        drop = matched;
        climbDirection = triggerDirection;
        state.OfferedTriggerDirection = triggerDirection;
        return true;
    }

    private static bool FloorPathOpen(Vector2 start, Vector2 end, FloorMap floor)
    {
        var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(start, end)));
        for (var index = 0; index <= steps; index++)
        {
            var point = Vector2.Lerp(start, end, index / (float)steps);
            if (!floor.Contains(point) || floor.CellAt(point) == 0)
            {
                return false;
            }
        }

        return true;
    }

    private void ValidateTarget(FieldZone current, WarpCallbacks callbacks)
    {
        if (_pendingExit is null)
        {
            throw new InvalidOperationException("warp validation without pending exit");
        }

        try
        {
            var target = callbacks.LoadTarget(_pendingExit.Destination);
            if (!target.Zone.Exits.Any(exit => exit.Destination == current.Zone.Id))
            {
                throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"zone={_pendingExit.Destination} reciprocal exit unavailable");
            }

            _pendingTarget = target;
            _fadeRemaining = FadeTicks;
            Transition(WarpState.Fade, callbacks.Info);
        }
        catch (CacheResourceException error)
        {
            callbacks.Warn("warp-cache-unavailable", $"CACHE_RESOURCE_UNAVAILABLE zone={_pendingExit.Destination} detail={error.Message}");
            _pendingExit = null;
            _pendingTarget = null;
            Transition(WarpState.Release, callbacks.Info);
        }
    }

    private void Commit(GameState state, WarpCallbacks callbacks)
    {
        if (_pendingExit is null || _pendingTarget is null)
        {
            throw new InvalidOperationException("warp commit without a validated target");
        }

        state.ZoneId = _pendingExit.Destination;
        state.Position = new Vector2(_pendingExit.X, _pendingExit.Y);
        state.SetFacing(_pendingExit.Facing);
        state.AnimationDirection = state.Facing;
        state.AnimationSet = 1;
        state.AnimationTicks = 0;
        state.Offer = false;
        CommitCount++;
        callbacks.ApplyScene(_pendingTarget);
        callbacks.Info($"warp commit={CommitCount} zone={state.ZoneId} position=({state.Position.X:0},{state.Position.Y:0}) facing={state.Facing}");
        Transition(WarpState.Release, callbacks.Info);
    }

    private void Transition(WarpState next, Action<string> info)
    {
        info($"warp state={State}->{next}");
        State = next;
    }
}
