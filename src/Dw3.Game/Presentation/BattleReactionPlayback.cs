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
    private Action? _completed;

    /// <summary>Initializes a new instance of the <see cref="BattleReactionPlayback"/> class.</summary>
    /// <param name="scripts">Pack-backed source of reaction scripts.</param>
    /// <param name="consumer">Physical consumer shared with the owning action.</param>
    public BattleReactionPlayback(IActionScriptProvider scripts, BattleRendererSequencePlayback consumer)
    {
        _scripts = scripts;
        _consumer = consumer;
    }

    /// <summary>Gets or sets the observer of actual reaction starts and completions.</summary>
    public Action<BattleHitEvent, int, int, bool>? DiagnosticReaction { get; set; }

    /// <summary>Gets a value indicating whether the child completed.</summary>
    public bool IsComplete => _timeline?.IsComplete != false;

    /// <summary>Starts one target reaction chosen from frozen hit facts.</summary>
    /// <param name="request">Stable hit-dispatch request.</param>
    /// <returns>Applied only after the real reaction script is validated and bound.</returns>
    public SequenceAcknowledgement Begin(HitDispatchedEvent request)
    {
        if (!IsComplete)
        {
            return SequenceAcknowledgement.Pending;
        }

        var hits = request.HitEvents.Where(hit => !hit.IsCounter && hit.AttackerSide == request.AttackerSide).ToArray();
        if (hits.Length == 0)
        {
            throw new FormatException("Reaction requires a resolved hit result.");
        }

        if (_hitCursor >= hits.Length)
        {
            return SequenceAcknowledgement.Applied;
        }

        int index = request.DispatchIndex == 5 ? _hitCursor : hits.Length - 1;
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
        DiagnosticReaction?.Invoke(hit, reactingFighter, selector, false);
        _completed = () => DiagnosticReaction?.Invoke(hit, reactingFighter, selector, true);
        _hitCursor = request.DispatchIndex == 5 ? _hitCursor + 1 : hits.Length;
        return SequenceAcknowledgement.Applied;
    }

    /// <summary>Advances the reaction child on the physical model clock.</summary>
    public void Step()
    {
        _timeline?.Advance();
        if (_timeline?.IsComplete == true)
        {
            _completed?.Invoke();
            _completed = null;
        }
    }

    /// <summary>Cancels child playback and resets the per-action multi-hit cursor.</summary>
    public void Clear()
    {
        _timeline?.Cancel();
        _timeline = null;
        _completed = null;
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
