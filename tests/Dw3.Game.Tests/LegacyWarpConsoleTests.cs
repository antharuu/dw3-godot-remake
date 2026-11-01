using Xunit;

namespace Dw3.Game.Tests;

using System.Numerics;

using Dw3Production.Conversion;
using Dw3Production.Simulation;

public sealed class LegacyWarpConsoleTests
{

    [Fact]
    public void PreservesLegacyConsoleAssertions()
    {
        CrossesBothDirectionsAndRearms();
        RejectsIncompatibleSourceFacingFromActualM1Triggers();
        AdmitsEverySourceNearDirectionFromActualM1Triggers();
        OffersNearbyReachableExitButRejectsSeparatedCell();
        RequiresCrossEdgeAfterSourceOffer();
        RejectsHeldRepeat();
        PreservesCurrentZoneWhenTargetUnavailable();
        OfferRadiusAllowsProximityBreakBeforeAdmissionPhase();

        OffersGatedExitAsUnevaluatedWithWarning();
        RejectsNonPhysicalGatedSlotWithSuppressedDispatch();
        OffersAndDispatchesClimbSlot();
    }

    private static void CrossesBothDirectionsAndRearms()
    {
        var s200 = Zone("S200", 4, "S235", 278, 218, 3, 3);
        var s235 = Zone("S235", 1, "S200", 672, 378, 7, 7);
        var state = new GameState { ZoneId = "S200", Position = Vector2.Zero, Facing = 3 };
        var input = new InputState();
        var warp = new WarpController();
        var current = s200;
        var logs = new List<string>();

        Tick(warp, state, input, current, target => target == "S235" ? s235 : s200, zone => current = zone, logs);
        Check(warp.State == WarpState.Offer && state.Offer, "offer admitted on S200 slot4");
        input.PressAction();
        RunUntilRearm(warp, state, input, () => current, target => target == "S235" ? s235 : s200, zone => current = zone, logs);
        Check(state.ZoneId == "S235" && state.Position == new Vector2(278, 218) && state.Facing == 3, "S200 slot4 commits S235 manifest arrival");
        Check(warp.CommitCount == 1, "first edge commits exactly once");
        input.ReleaseAction();
        Tick(warp, state, input, current, target => target == "S235" ? s235 : s200, zone => current = zone, logs);
        Check(warp.State == WarpState.Idle, "release rearms controller");

        state.Position = Vector2.Zero;
        state.SetFacing(7);
        Tick(warp, state, input, current, target => target == "S235" ? s235 : s200, zone => current = zone, logs);
        input.PressAction();
        RunUntilRearm(warp, state, input, () => current, target => target == "S235" ? s235 : s200, zone => current = zone, logs);
        Check(state.ZoneId == "S200" && state.Position == new Vector2(672, 378) && state.Facing == 7, "S235 slot1 commits S200 manifest arrival");
        Check(warp.CommitCount == 2, "roundtrip commits each edge once");
        input.ReleaseAction();
        Tick(warp, state, input, current, target => target == "S235" ? s235 : s200, zone => current = zone, logs);
    }

    private static void RejectsIncompatibleSourceFacingFromActualM1Triggers()
    {
        // The external M1 cache records live raw cells 0x64 (S200 slot 4, dir 3)
        // and 0xE1 (S235 slot 1, dir 7). Source nearDirs admits only dir ±1.
        Check(!OffersRawM1Exit(0x64, actorFacing: 1), "S200 raw 0x64 rejects incompatible source facing=1");
        Check(!OffersRawM1Exit(0xE1, actorFacing: 3), "S235 raw 0xE1 rejects incompatible source facing=3");
    }

    private static void AdmitsEverySourceNearDirectionFromActualM1Triggers()
    {
        foreach (var facing in new[] { 2, 3, 4 })
            Check(OffersRawM1Exit(0x64, facing), $"S200 raw 0x64 admits source facing={facing}");
        foreach (var facing in new[] { 6, 7, 0 })
            Check(OffersRawM1Exit(0xE1, facing), $"S235 raw 0xE1 admits source facing={facing}");
    }

    private static bool OffersRawM1Exit(byte raw, int actorFacing)
    {
        var slot = raw & 0x1F;
        var zone = Zone("M1", slot, "Target", 1, 1, 0, raw >> 5);
        var state = new GameState { ZoneId = "M1", Position = Vector2.Zero, Facing = actorFacing };
        var warp = new WarpController();
        var logs = new List<string>();
        Tick(warp, state, new InputState(), zone, _ => zone, _ => { }, logs);
        return warp.State == WarpState.Offer && state.Offer;
    }

    private static void OffersNearbyReachableExitButRejectsSeparatedCell()
    {
        var zone = new Zone("M1", 1, 5, [], "floor", "trigger", [new Exit(4, "Target", 1, 1, 3)], [], "synthetic");
        var floor = new FloorMap(5, 1, [1, 1, 1, 1, 1]);
        var triggers = new TriggerMap(5, 1, [0, 0, 0, 0x64, 0]);
        var state = new GameState { ZoneId = "M1", Position = new Vector2(1, 0), Facing = 3 };
        var warp = new WarpController();
        Tick(warp, state, new InputState(), new FieldZone(zone, floor, triggers, []), _ => throw new InvalidOperationException(), _ => { }, []);
        Check(warp.State == WarpState.Offer && state.OfferedTriggerDirection == 3, "reachable exit offers within the configured two-unit margin with resolved cue direction");

        var blockedFloor = new FloorMap(5, 1, [1, 1, 0, 1, 1]);
        state = new GameState { ZoneId = "M1", Position = new Vector2(1, 0), Facing = 3 };
        warp = new WarpController();
        Tick(warp, state, new InputState(), new FieldZone(zone, blockedFloor, triggers, []), _ => throw new InvalidOperationException(), _ => { }, []);
        Check(warp.State == WarpState.Idle && !state.Offer, "nearby exit behind a void separator is not offered");
    }

    private static void RequiresCrossEdgeAfterSourceOffer()
    {
        var s200 = Zone("S200", 4, "S235", 278, 218, 3, 3);
        var s235 = Zone("S235", 1, "S200", 672, 378, 7, 7);
        var state = new GameState { ZoneId = "S200", Position = Vector2.Zero, Facing = 3 };
        var input = new InputState();
        var warp = new WarpController();
        var current = s200;
        var logs = new List<string>();

        // FIELDSTG_updateTriggers offers in substate 0, then only checks PAD_CROSS
        // in substate 1 on a later update. A Cross edge on the offer tick is lost.
        input.PressAction();
        Tick(warp, state, input, current, target => target == "S235" ? s235 : s200, zone => current = zone, logs);
        Check(warp.State == WarpState.Offer && warp.CommitCount == 0, "Cross edge on offer tick does not bypass source offer state");
        input.ReleaseAction();
        Tick(warp, state, input, current, target => target == "S235" ? s235 : s200, zone => current = zone, logs);
        input.PressAction();
        RunUntilRearm(warp, state, input, () => current, target => target == "S235" ? s235 : s200, zone => current = zone, logs);
        Check(state.ZoneId == "S235" && warp.CommitCount == 1, "Cross edge after offer commits source exit");
        input.ReleaseAction();
    }

    private static void RejectsHeldRepeat()
    {
        var s200 = Zone("S200", 4, "S235", 278, 218, 3, 3);
        var s235 = Zone("S235", 1, "S200", 672, 378, 7, 7);
        var state = new GameState { ZoneId = "S200", Position = Vector2.Zero, Facing = 3 };
        var input = new InputState();
        var warp = new WarpController();
        var current = s200;
        var logs = new List<string>();
        Tick(warp, state, input, current, target => s235, zone => current = zone, logs);
        input.PressAction();
        RunUntilRearm(warp, state, input, () => current, target => s235, zone => current = zone, logs);
        Check(warp.CommitCount == 1 && warp.State == WarpState.Rearm, "held action reaches rearm after one commit");
        for (var i = 0; i < 5; i++) Tick(warp, state, input, current, target => s235, zone => current = zone, logs);
        Check(warp.CommitCount == 1 && warp.State == WarpState.Rearm, "held action cannot re-fire before release");
    }

    private static void PreservesCurrentZoneWhenTargetUnavailable()
    {
        var s200 = Zone("S200", 4, "S235", 278, 218, 3, 3);
        var state = new GameState { ZoneId = "S200", Position = Vector2.Zero, Facing = 3 };
        var input = new InputState();
        var warp = new WarpController();
        var logs = new List<string>();
        Tick(warp, state, input, s200, _ => throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "synthetic missing target"), _ => throw new InvalidOperationException("scene must not swap"), logs);
        input.PressAction();
        for (var i = 0; i < 3; i++) Tick(warp, state, input, s200, _ => throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "synthetic missing target"), _ => throw new InvalidOperationException("scene must not swap"), logs);
        Check(state.ZoneId == "S200" && state.Position == Vector2.Zero && warp.CommitCount == 0, "unavailable target leaves prior scene state intact");
        Check(logs.Any(line => line.Contains("CACHE_RESOURCE_UNAVAILABLE zone=S235", StringComparison.Ordinal)), "unavailable target names destination in diagnostic");
    }

    // Regression: 972e39b changed WalkToExitThroughInput's main loop to stop on
    // WarpState.Offer instead of trigger proximity.  WarpState.Offer requires
    // facing admission, which only runs AFTER the main loop — so the loop would
    // never exit and the test failed after 2400 walk ticks.
    // Fix: loop breaks on NearTriggerSlot (2-unit radius), admission phase remains
    // reachable.  This test verifies TryOffer fires at the 2-unit boundary when the
    // player already has an admitted facing, without a prior explicit admission phase.
    private static void OfferRadiusAllowsProximityBreakBeforeAdmissionPhase()
    {
        // 5-cell floor; trigger slot=4, direction=3 at cell x=3.  Player at x=1:
        // distance = 2.0 exactly — within OfferRadiusWorldUnits.  Facing=3 is
        // admitted for triggerDirection=3 per SourceNearDirections[3,3]=true.
        var zone = new Zone("M1", 5, 1, [], "floor", "trigger", [new Exit(4, "T", 1, 1, 3)], [], "synthetic");
        var floor = new FloorMap(5, 1, [1, 1, 1, 1, 1]);
        var triggers = new TriggerMap(5, 1, [0, 0, 0, 0x64, 0]);
        var state = new GameState { ZoneId = "M1", Position = new Vector2(1, 0), Facing = 3 };
        var warp = new WarpController();
        Tick(warp, state, new InputState(), new FieldZone(zone, floor, triggers, []), _ => throw new InvalidOperationException(), _ => { }, []);
        Check(warp.State == WarpState.Offer, "player at distance=2 with admitted facing fires Offer without explicit admission phase");
        Check(state.OfferedTriggerDirection == 3, "OfferedTriggerDirection records source trigger direction for cue");
        // Player at 2.01 units away should NOT offer (just outside radius).
        state = new GameState { ZoneId = "M1", Position = new Vector2(0.99f, 0), Facing = 3 };
        warp = new WarpController();
        Tick(warp, state, new InputState(), new FieldZone(zone, floor, triggers, []), _ => throw new InvalidOperationException(), _ => { }, []);
        Check(warp.State == WarpState.Idle, "player at distance=2.01 does not offer (just outside radius)");
    }


    private static void OffersGatedExitAsUnevaluatedWithWarning()
    {
        // A gated SLOT_EXIT is now a live, crossable edge marked unevaluated. Walking
        // onto its trigger cell offers normally and logs exit-condition-not-enforced
        // once, carrying the raw condition, instead of suppressing the dispatch.
        var exit = new Exit(1, "T", 1, 1, 3, 0x7007, "unevaluated");
        var zone = new Zone("M1", 3, 1, [], "floor", "trigger", [exit], [], "synthetic");
        var floor = new FloorMap(3, 1, [1, 1, 1]);
        var triggers = new TriggerMap(3, 1, [0, (byte)((3 << 5) | 1), 0]);
        var state = new GameState { ZoneId = "M1", Position = new Vector2(1, 0), Facing = 3 };
        var warp = new WarpController();
        var logs = new List<string>();
        Tick(warp, state, new InputState(), new FieldZone(zone, floor, triggers, []), _ => throw new InvalidOperationException(), _ => { }, logs);
        Check(warp.State == WarpState.Offer && state.Offer, "gated SLOT_EXIT offers normally");
        Check(logs.Any(line => line.StartsWith("exit-condition-not-enforced:", StringComparison.Ordinal)
                               && line.Contains("zone=M1", StringComparison.Ordinal)
                               && line.Contains("slot=1", StringComparison.Ordinal)
                               && line.Contains("condition=0x7007", StringComparison.Ordinal)),
            "gated SLOT_EXIT logs exit-condition-not-enforced with raw condition");
        // First occurrence only: a second tick in Offer state must not duplicate the warning.
        Tick(warp, state, new InputState(), new FieldZone(zone, floor, triggers, []), _ => throw new InvalidOperationException(), _ => { }, logs);
        Check(logs.Count(line => line.StartsWith("exit-condition-not-enforced:", StringComparison.Ordinal)) == 1,
            "gated SLOT_EXIT warning logs once per zone:slot");
    }

    private static void RejectsNonPhysicalGatedSlotWithSuppressedDispatch()
    {
        // A gated SLOT_EVENT (non-physical: event/warp/gauge) is not an exit edge, so it
        // stays absent from Exits. Walking onto its trigger cell must not offer and must
        // log suppressed-dispatch with the unimplemented reason, leaving the scene intact.
        var zone = new Zone("M1", 3, 1, [], "floor", "trigger", [], [], "synthetic");
        var floor = new FloorMap(3, 1, [1, 1, 1]);
        var triggers = new TriggerMap(3, 1, [0, (byte)((3 << 5) | 1), 0]);
        var state = new GameState { ZoneId = "M1", Position = new Vector2(1, 0), Facing = 3 };
        var warp = new WarpController();
        var logs = new List<string>();
        Tick(warp, state, new InputState(), new FieldZone(zone, floor, triggers, []), _ => throw new InvalidOperationException(), _ => { }, logs);
        Check(warp.State == WarpState.Idle && !state.Offer, "gated non-physical slot does not offer");
        Check(logs.Any(line => line.Contains("suppressed-dispatch", StringComparison.Ordinal) && line.Contains("exit-unimplemented", StringComparison.Ordinal)),
            "gated non-physical slot logs suppressed-dispatch with unimplemented reason");
    }

    private static void OffersAndDispatchesClimbSlot()
    {
        // A SLOT_CLIMB_UP (type 2) offers exactly like an exit, records the source
        // trigger direction for the cue, and a Cross press dispatches the climb slot
        // with that direction instead of leaving the field (trigger.c setOffTrigger).
        var climbSlot = new Climb(2, 2, 100, 100, 32);
        var zone = new Zone("M1", 3, 1, [], "floor", "trigger", [], [climbSlot], "synthetic");
        var floor = new FloorMap(3, 1, [1, 1, 1]);
        var triggers = new TriggerMap(3, 1, [0, (byte)((3 << 5) | 2), 0]);
        var state = new GameState { ZoneId = "M1", Position = new Vector2(1, 0), Facing = 3 };
        var input = new InputState();
        var warp = new WarpController();
        var dispatched = new List<(Climb, int)>();
        void TickClimb(FieldZone field) => warp.Tick(state, input, field, new WarpCallbacks(_ => throw new InvalidOperationException(), _ => { }, _ => { }, (key, msg) => { }, (climb, dir) => dispatched.Add((climb, dir))));

        TickClimb(new FieldZone(zone, floor, triggers, []));
        Check(warp.State == WarpState.Offer && state.Offer, "climb slot offers like an exit");
        Check(state.OfferedTriggerDirection == 3, "climb offer records the source trigger direction");
        input.PressAction();
        TickClimb(new FieldZone(zone, floor, triggers, []));
        Check(warp.State == WarpState.Idle && !state.Offer, "climb dispatch returns the trigger to idle");
        Check(dispatched.Count == 1 && dispatched[0].Item1 == climbSlot && dispatched[0].Item2 == 3,
            "cross press dispatches the climb slot with its trigger direction");
    }



    private static FieldZone Zone(string id, int slot, string destination, int x, int y, int facing, int triggerFacing)
    {
        var zone = new Zone(id, 1, 1, [], "floor", "trigger", [new Exit(slot, destination, x, y, facing)], [], "synthetic");
        return new FieldZone(zone, new FloorMap(1, 1, [1]), new TriggerMap(1, 1, [(byte)((triggerFacing << 5) | slot)]), []);
    }

    private static void RunUntilRearm(WarpController warp, GameState state, InputState input, Func<FieldZone> current, Func<string, FieldZone> load, Action<FieldZone> apply, List<string> logs)
    {
        for (var i = 0; i < 12 && warp.State != WarpState.Rearm; i++) Tick(warp, state, input, current(), load, apply, logs);
        Check(warp.State == WarpState.Rearm, "warp reaches Rearm in bounded ticks");
    }

    private static void Tick(WarpController warp, GameState state, InputState input, FieldZone current, Func<string, FieldZone> load, Action<FieldZone> apply, List<string> logs) =>
        warp.Tick(state, input, current, new WarpCallbacks(load, apply, logs.Add, (key, message) => logs.Add($"{key}:{message}"), (climb, dir) => throw new InvalidOperationException("unexpected climb dispatch in warp test")));

    private static void Check(bool condition, string name)
    {
        Assert.True(condition, name);
    }
}
