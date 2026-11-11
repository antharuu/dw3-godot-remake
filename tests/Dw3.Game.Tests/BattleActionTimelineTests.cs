using Dw3.Data.Battle;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Unit tests for BattleActionTimeline — the engine-free sequence executor.</summary>
public sealed class BattleActionTimelineTests
{
    [Fact]
    public void MotionZero_WaitsWithoutPublishingImpactOrEnd()
    {
        var script = MinimalScript(
            (BattleActionOpcode.Model, [0, 0, 0]),
            (BattleActionOpcode.HitDispatch, [0]),
            (BattleActionOpcode.AlternateEnd, []));
        var timeline = new BattleActionTimeline(script, TestContext());

        Assert.Empty(timeline.Advance());
        Assert.Empty(timeline.Advance(10));
        Assert.Equal(0, timeline.Cursor);
        Assert.False(timeline.IsComplete);
    }

    [Fact]
    public void MotionReady_ReleasesImpactOnceWithoutMotionZeroRequest()
    {
        var script = MinimalScript(
            (BattleActionOpcode.Model, [0, 0, 0]),
            (BattleActionOpcode.HitDispatch, [0]),
            (BattleActionOpcode.End, []));
        var timeline = new BattleActionTimeline(script, TestContext());
        bool ready = false;
        timeline.MotionReady = _ => ready;
        Assert.Empty(timeline.Advance(20));
        ready = true;
        Assert.Collection(timeline.Advance(), e => Assert.IsType<HitDispatchedEvent>(e), e => Assert.IsType<SequenceEndedEvent>(e));
        Assert.Empty(timeline.Advance(20));
    }

    [Fact]
    public void UnsupportedAndUnterminatedScripts_RejectBeforeAnyEffect()
    {
        Assert.Throws<FormatException>(() => new BattleActionTimeline(
            MinimalScript(((BattleActionOpcode)123, []), (BattleActionOpcode.End, [])), TestContext()));
        Assert.Throws<FormatException>(() => new BattleActionTimeline(
            MinimalScript((BattleActionOpcode.Sound, [1, 0])), TestContext()));
        Assert.Throws<FormatException>(() => new BattleActionTimeline(
            MinimalScript((BattleActionOpcode.Model, [0]), (BattleActionOpcode.End, [])), TestContext()));
    }

    [Fact]
    public void AcknowledgementAndChildren_BlockCursorAndEndIndependently()
    {
        var script = MinimalScript((BattleActionOpcode.Sound, [1, 0]), (BattleActionOpcode.End, []));
        bool accepted = false;
        bool childrenDone = false;
        var timeline = new BattleActionTimeline(script, TestContext())
        {
            Acknowledge = _ => accepted,
            ChildrenReady = () => childrenDone,
        };
        Assert.Empty(timeline.Advance(10));
        Assert.Equal(0, timeline.Cursor);
        accepted = true;
        Assert.IsType<SoundRequestEvent>(Assert.Single(timeline.Advance()));
        Assert.False(timeline.IsComplete);
        Assert.Empty(timeline.Advance(10));
        childrenDone = true;
        Assert.IsType<SequenceEndedEvent>(Assert.Single(timeline.Advance()));
        Assert.Empty(timeline.Advance());
    }

    [Fact]
    public void RequiredScriptWithoutDamageEventsStillGetsAContext()
    {
        var context = ActionSequenceContext.TryCapture(BattleSide.Player, new TechData { Script = 5 }, 10, 20, []);
        Assert.NotNull(context);
        Assert.Equal(5, context.ScriptSelector);
        Assert.Null(ActionSequenceContext.TryCapture(BattleSide.Player, new TechData { Script = 0 }, 10, 20, []));
    }

    [Fact]
    public void PendingConsumerReceivesTheSameRequestUntilAcknowledged()
    {
        var timeline = new BattleActionTimeline(MinimalScript((BattleActionOpcode.Sound, [1, 0]), (BattleActionOpcode.End, [])), TestContext());
        var observed = new List<BattleSequenceEvent>();
        timeline.Acknowledge = request =>
        {
            observed.Add(request);
            return false;
        };
        timeline.Advance(3);
        Assert.Equal(3, observed.Count);
        Assert.Same(observed[0], observed[1]);
        Assert.Same(observed[1], observed[2]);
    }

    [Fact]
    public void PreResolvedMissZeroDamageAndCounterRemainDistinct()
    {
        BattleHitEvent[] hits =
        [
            new(BattleSide.Player, 0, false, 0, 0, false),
            new(BattleSide.Player, 1, true, 0, 0, false),
            new(BattleSide.Enemy, 0, true, 8, 0, true),
        ];
        var context = new ActionSequenceContext(BattleSide.Player, 10, 20, hits, 5);
        var timeline = new BattleActionTimeline(MinimalScript((BattleActionOpcode.HitDispatch, [5]), (BattleActionOpcode.End, [])), context);
        var dispatched = Assert.Single(timeline.Advance().OfType<HitDispatchedEvent>());
        Assert.False(dispatched.HitEvents[0].Landed);
        Assert.True(dispatched.HitEvents[1].Landed);
        Assert.Equal(0, dispatched.HitEvents[1].Damage);
        Assert.True(dispatched.HitEvents[2].IsCounter);
        Assert.Equal(BattleSide.Enemy, dispatched.HitEvents[2].AttackerSide);
        Assert.Empty(timeline.Advance(20));
    }

    // ── Completion and state ──────────────────────────────────────────────────
    [Fact]
    public void EndOpcode_TerminatesImmediately()
    {
        var script = MinimalScript((BattleActionOpcode.End, []));
        var timeline = new BattleActionTimeline(script, TestContext());

        var events = timeline.Advance(1);

        Assert.True(timeline.IsComplete);
        var ended = Assert.Single(events);
        Assert.IsType<SequenceEndedEvent>(ended);
    }

    [Fact]
    public void AlternateEnd_TerminatesLikeEnd()
    {
        var script = MinimalScript((BattleActionOpcode.AlternateEnd, []));
        var timeline = new BattleActionTimeline(script, TestContext());

        var events = timeline.Advance(1);

        Assert.True(timeline.IsComplete);
        Assert.Single(events, e => e is SequenceEndedEvent);
    }

    [Fact]
    public void Advance_AfterComplete_ReturnsEmpty()
    {
        var script = MinimalScript((BattleActionOpcode.End, []));
        var timeline = new BattleActionTimeline(script, TestContext());
        timeline.Advance(1);

        var events = timeline.Advance(1);

        Assert.Empty(events);
    }

    [Fact]
    public void Cancel_MarksComplete_NoFurtherEvents()
    {
        var script = MinimalScript((BattleActionOpcode.Wait, [100]), (BattleActionOpcode.End, []));
        var timeline = new BattleActionTimeline(script, TestContext());

        timeline.Cancel();
        var events = timeline.Advance(1);

        Assert.True(timeline.IsComplete);
        Assert.Empty(events);
    }

    // ── Wait synchronization ──────────────────────────────────────────────────
    [Fact]
    public void Wait_BlocksUntilFrameCountExpired()
    {
        var script = MinimalScript((BattleActionOpcode.Wait, [3]), (BattleActionOpcode.End, []));
        var timeline = new BattleActionTimeline(script, TestContext());

        var frame1 = timeline.Advance(1);
        var frame2 = timeline.Advance(1);

        // Frame 1: Wait starts, 3 → remaining becomes 2 (wait-1 on first frame)
        // Frame 2: countdown, one more frame before End fires
        Assert.Empty(frame1);
        Assert.Empty(frame2);
        Assert.False(timeline.IsComplete);

        Assert.Empty(timeline.Advance(1));
        var frame3 = timeline.Advance(1);

        // Frame 3: wait expired, End fires
        Assert.True(timeline.IsComplete);
        Assert.Single(frame3, e => e is SequenceEndedEvent);
    }

    [Fact]
    public void WaitZero_InitializesBeforeNextTickCompletes()
    {
        var script = MinimalScript((BattleActionOpcode.Wait, [0]), (BattleActionOpcode.End, []));
        var timeline = new BattleActionTimeline(script, TestContext());

        Assert.Empty(timeline.Advance(1));
        var events = timeline.Advance(1);

        Assert.True(timeline.IsComplete);
        Assert.Single(events, e => e is SequenceEndedEvent);
    }

    [Fact]
    public void NegativeWait_AdvanceArgument_Throws()
    {
        var script = MinimalScript((BattleActionOpcode.End, []));
        var timeline = new BattleActionTimeline(script, TestContext());

        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.Advance(0));
    }

    // ── HitDispatch event ─────────────────────────────────────────────────────
    [Fact]
    public void HitDispatch_EmitsPreResolvedHits_AtThatScriptPosition()
    {
        var hit = new BattleHitEvent(BattleSide.Player, 0, true, 42, 0, false);
        var ctx = ContextWithHits([hit]);
        var script = MinimalScript(
            (BattleActionOpcode.HitDispatch, [0]),
            (BattleActionOpcode.End, []));
        var timeline = new BattleActionTimeline(script, ctx);

        var events = timeline.Advance(1);

        var dispatch = Assert.Single(events, e => e is HitDispatchedEvent);
        var d = Assert.IsType<HitDispatchedEvent>(dispatch);
        Assert.Equal(BattleSide.Player, d.AttackerSide);
        Assert.Equal(1, d.AttackerId);
        Assert.Equal(2, d.TargetId);
        Assert.Equal(42, d.HitEvents[0].Damage);
    }

    [Fact]
    public void HitDispatch_FrozenIdentity_SurvivedBeforeRelay()
    {
        // Identities must match the context (frozen at sequence start), not live state
        var ctx = new ActionSequenceContext(BattleSide.Enemy, 99, 77, [], 5);
        var script = MinimalScript((BattleActionOpcode.HitDispatch, [1]), (BattleActionOpcode.End, []));
        var timeline = new BattleActionTimeline(script, ctx);

        var events = timeline.Advance(1);

        var dispatch = events.OfType<HitDispatchedEvent>().Single();
        Assert.Equal(99, dispatch.AttackerId);
        Assert.Equal(77, dispatch.TargetId);
        Assert.Equal(1, dispatch.DispatchIndex);
    }

    // ── Camera ownership ─────────────────────────────────────────────────────
    [Fact]
    public void Camera_EmitsCameraRequestEvent()
    {
        var script = MinimalScript(
            (BattleActionOpcode.Camera, [0, 0, 0]),
            (BattleActionOpcode.End, []));
        var timeline = new BattleActionTimeline(script, TestContext());

        var events = timeline.Advance(1);

        Assert.Contains(events, e => e is CameraRequestEvent);
    }

    // ── Typed events for other opcodes ───────────────────────────────────────
    [Fact]
    public void ModelOpcode_EmitsModelMotionEvent()
    {
        var script = MinimalScript((BattleActionOpcode.Model, [0, 1, 14]), (BattleActionOpcode.End, []));
        var timeline = new BattleActionTimeline(script, TestContext());

        var events = timeline.Advance(1);

        var motion = events.OfType<ModelMotionEvent>().Single();
        Assert.Equal(0, motion.Subcommand);
        Assert.Equal(1, motion.Actor);
        Assert.Equal(14, motion.Motion);
    }

    [Fact]
    public void SoundOpcode_EmitsSoundRequestEvent()
    {
        var script = MinimalScript((BattleActionOpcode.Sound, [89, 0]), (BattleActionOpcode.End, []));
        var timeline = new BattleActionTimeline(script, TestContext());

        var events = timeline.Advance(1);

        var sound = events.OfType<SoundRequestEvent>().Single();
        Assert.Equal(89, sound.SoundId);
    }

    // ── Multi-frame and multi-command sequences ───────────────────────────────
    [Fact]
    public void Advance_MultipleFrames_SameAsFrameByFrame()
    {
        var script = MinimalScript(
            (BattleActionOpcode.Sound, [1, 0]),
            (BattleActionOpcode.Wait, [2]),
            (BattleActionOpcode.Sound, [2, 0]),
            (BattleActionOpcode.End, []));

        var timelineMulti = new BattleActionTimeline(script, TestContext());
        var timelineSingle = new BattleActionTimeline(script, TestContext());

        // Multi-frame: advance 4 frames at once
        var multiEvents = timelineMulti.Advance(4);

        // Single-frame: advance 1 frame at a time
        var singleEvents = new List<BattleSequenceEvent>();
        for (int i = 0; i < 4; i++)
        {
            singleEvents.AddRange(timelineSingle.Advance(1));
        }

        Assert.Equal(multiEvents.Count, singleEvents.Count);
        Assert.True(timelineMulti.IsComplete);
        Assert.True(timelineSingle.IsComplete);
    }

    // ── Sequence context identity ─────────────────────────────────────────────
    [Fact]
    public void Context_IsAccessibleFromTimeline()
    {
        var ctx = TestContext(BattleSide.Enemy, 55, 66);
        var script = MinimalScript((BattleActionOpcode.End, []));
        var timeline = new BattleActionTimeline(script, ctx);

        Assert.Equal(55, timeline.Context.AttackerId);
        Assert.Equal(66, timeline.Context.TargetId);
        Assert.Equal(BattleSide.Enemy, timeline.Context.AttackerSide);
    }

    private static BattleActionScript MinimalScript(params (BattleActionOpcode, short[])[] entries)
    {
        var commands = new List<BattleActionCommand>();
        foreach (var (op, args) in entries)
        {
            commands.Add(new BattleActionCommand(op, args));
        }

        return new BattleActionScript(0, commands);
    }

    private static ActionSequenceContext TestContext(BattleSide side = BattleSide.Player, int attackerId = 1, int targetId = 2) =>
        new(side, attackerId, targetId, [], 5);

    private static ActionSequenceContext ContextWithHits(BattleHitEvent[] hits) =>
        new(BattleSide.Player, 1, 2, hits, 5);
}
