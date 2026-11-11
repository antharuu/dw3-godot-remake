using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>Runs the target's extracted reaction script without resolving combat rules again.</summary>
internal sealed class BattleReactionPlayback
{
    private readonly IActionScriptProvider _scripts;
    private readonly BattleRendererSequencePlayback _consumer;
    private BattleActionTimeline? _timeline;
    private int _hitCursor;

    /// <summary>Initializes a new instance of the <see cref="BattleReactionPlayback"/> class.</summary>
    /// <param name="scripts">Pack-backed source of reaction scripts.</param>
    /// <param name="consumer">Physical consumer shared with the owning action.</param>
    public BattleReactionPlayback(IActionScriptProvider scripts, BattleRendererSequencePlayback consumer)
    {
        _scripts = scripts;
        _consumer = consumer;
    }

    /// <summary>Gets a value indicating whether the child completed.</summary>
    public bool IsComplete => _timeline?.IsComplete != false;

    /// <summary>Starts one target reaction chosen from frozen hit facts.</summary>
    /// <param name="request">Stable hit-dispatch request.</param>
    /// <returns>Applied only after the real reaction script is validated and bound.</returns>
    public SequenceAcknowledgement Begin(HitDispatchedEvent request)
    {
        var hits = request.HitEvents.Where(hit => !hit.IsCounter && hit.AttackerSide == request.AttackerSide).ToArray();
        if (hits.Length == 0)
        {
            throw new FormatException("Reaction requires a resolved hit result.");
        }

        int index = request.DispatchIndex == 5 ? Math.Min(_hitCursor, hits.Length - 1) : hits.Length - 1;
        var hit = hits[index];
        int selector = hit.TargetDefeated ? 3 : 2;
        if (!hit.Landed)
        {
            selector = 4;
        }

        var script = _scripts.TryLoad(request.TargetId, selector)
            ?? throw new FormatException($"Reaction script unavailable fighter={request.TargetId} selector={selector}.");
        var side = request.AttackerSide == BattleSide.Player ? BattleSide.Enemy : BattleSide.Player;
        int reactingFighter = request.TargetId;
        int opposingFighter = request.AttackerId;
        var context = new ActionSequenceContext(side, reactingFighter, opposingFighter, [], selector);
        _timeline = new BattleActionTimeline(script, context)
        {
            MotionReady = actor => _consumer.MotionReady(side, actor),
            JumpReady = actor => _consumer.JumpReady(side, actor),
            ChildrenReady = () => _consumer.JumpReady(side, 0),
            Acknowledge = Consume,
        };
        _hitCursor += request.DispatchIndex == 5 ? 1 : 0;
        return SequenceAcknowledgement.Applied;
    }

    /// <summary>Advances the reaction child on the physical model clock.</summary>
    public void Step() => _timeline?.Advance();

    /// <summary>Cancels child playback and resets the per-action multi-hit cursor.</summary>
    public void Clear()
    {
        _timeline?.Cancel();
        _timeline = null;
        _hitCursor = 0;
    }

    /// <summary>Routes child events through the same physical consumers as their parent.</summary>
    /// <param name="request">Stable child request.</param>
    /// <returns>Whether the requested resource was physically applied.</returns>
    private bool Consume(BattleSequenceEvent request)
    {
        var result = _consumer.Consume(request);
        if (result == SequenceAcknowledgement.Unavailable)
        {
            throw new FormatException($"Reaction consumer unavailable: {request.GetType().Name}.");
        }

        return result == SequenceAcknowledgement.Applied;
    }
}
