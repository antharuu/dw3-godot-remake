using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Dw3Production.Simulation;

namespace Dw3Production;

/// <summary>Owns one action playback lifecycle without re-running rules or silently completing missing scripts.</summary>
internal sealed class BattleSequenceRunner
{
    private readonly BattleFlowController _flow;
    private readonly IActionScriptProvider _scripts;
    private readonly BattleState _state;
    private BattleActionTimeline? _timeline;
    private IBattleSequencePlayback? _playback;
    private ActionSequenceContext? _context;
    private bool _resultShown;

    /// <summary>Initializes a new instance of the <see cref="BattleSequenceRunner"/> class.</summary>
    /// <param name="flow">Active battle.</param>
    /// <param name="scripts">Pack-backed script provider.</param>
    public BattleSequenceRunner(BattleFlowController flow, IActionScriptProvider scripts)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(scripts);
        _flow = flow;
        _state = flow.State;
        _scripts = scripts;
    }

    /// <summary>Gets the recoverable failure, distinct from a normal physical wait.</summary>
    public string? Failure { get; private set; }

    /// <summary>
    /// Gets or sets the Cross source of the result message: the newly pressed state of this frame, or null when no
    /// player input owns the battle (an offscreen driver), which closes each page at the first frame the source
    /// reads Cross.
    /// </summary>
    public Func<bool?>? Confirm { get; set; }

    /// <summary>Gets the next command index for a private playback trace.</summary>
    public int DiagnosticCursor => _timeline?.Cursor ?? -1;

    /// <summary>Binds the physical consumer, including deterministic synchronization controls in tests.</summary>
    /// <param name="playback">Consumer which owns camera and physical callbacks.</param>
    public void SetPlayback(IBattleSequencePlayback playback) => _playback = playback;

    /// <summary>Binds the active renderer's sequence consumer.</summary>
    /// <param name="renderer">Renderer, or null during teardown.</param>
    public void SetRenderer(BattleRenderer? renderer)
    {
        _playback = renderer?.SequencePlayback;
        renderer?.SequencePlayback.ConfigureReactions(_scripts);
    }

    /// <summary>Advances the same playback seam called by the fixed-tick host.</summary>
    public void Tick()
    {
        if (_flow.State != _state || _flow.Phase != BattleFlowPhase.ActionSequence)
        {
            Cancel();
            return;
        }

        if (_resultShown)
        {
            StepResult();
            return;
        }

        if (_timeline is null && !TryStartTimeline())
        {
            return;
        }

        if (_timeline is not { } current)
        {
            return;
        }

        current.Advance();
        if (current.IsComplete)
        {
            _playback?.Finish(cancelled: false);
            _timeline = null;
            BeginResult();
        }
    }

    /// <summary>Cancels children and presentation ownership. Does not fabricate successful sequence completion.</summary>
    public void Cancel()
    {
        _timeline?.Cancel();
        _timeline = null;
        _playback?.Finish(cancelled: true);
        if (_resultShown)
        {
            _flow.CloseActionResult();
            _resultShown = false;
        }
    }

    /// <summary>Shows the finished action's result message, or completes the action when it has none.</summary>
    private void BeginResult()
    {
        if (_context is { } context && _flow.BeginActionResult(context))
        {
            _resultShown = true;
            _ = Confirm?.Invoke();
            return;
        }

        _flow.CompleteActionSequence();
    }

    /// <summary>Advances the result message and completes the action once its last page closed.</summary>
    private void StepResult()
    {
        bool confirm = Confirm?.Invoke() ?? _flow.ActionResult?.Reveal.Arrow == true;
        if (_flow.StepActionResult(confirm))
        {
            _resultShown = false;
            _flow.CloseActionResult();
            _flow.CompleteActionSequence();
        }
    }

    /// <summary>Loads and validates before consuming the context. Missing resources keep an explicit recoverable failure.</summary>
    /// <returns>Whether playback started.</returns>
    private bool TryStartTimeline()
    {
        var context = _flow.PendingSequenceContext;
        if (context is null)
        {
            Fail("action-sequence-context-missing");
            return false;
        }

        try
        {
            var script = _scripts.TryLoad(context.AttackerId, context.ScriptSelector);
            if (script is null)
            {
                Fail($"action-script-required-unavailable fighter={context.AttackerId} selector={context.ScriptSelector}");
                return false;
            }

            _timeline = new BattleActionTimeline(script, context)
            {
                MotionReady = actor => _playback?.MotionReady(actor) == true,
                JumpReady = actor => _playback?.JumpReady(actor) == true,
                ChildrenReady = () => _playback?.ChildrenReady != false,
                Acknowledge = Acknowledge,
            };
            _playback?.Begin(context);
            _context = context;
            _flow.ConsumeSequenceContext();
            Failure = null;
            return true;
        }
        catch (Exception error) when (error is IOException or FormatException or CacheResourceException)
        {
            Fail($"action-script-invalid: {error.Message}");
            return false;
        }
    }

    /// <summary>Routes every event family and only publishes consumer-confirmed application.</summary>
    /// <param name="request">Typed request.</param>
    /// <returns>Whether the command was consumed.</returns>
    private bool Acknowledge(BattleSequenceEvent request)
    {
        try
        {
            return AcknowledgeAvailable(request);
        }
        catch (Exception error) when (error is FormatException or IOException or CacheResourceException)
        {
            Fail($"action-resource-unavailable: {error.Message}");
            return false;
        }
    }

    /// <summary>Consumes one physical request without skipping unavailable backends.</summary>
    /// <param name="request">Stable request identity retained across pending retries.</param>
    /// <returns>Whether the consumer applied it.</returns>
    private bool AcknowledgeAvailable(BattleSequenceEvent request)
    {
        var result = _playback?.Consume(request) ??
            (request is SequenceEndedEvent ? SequenceAcknowledgement.Applied : SequenceAcknowledgement.Unavailable);
        if (result == SequenceAcknowledgement.Unavailable)
        {
            Fail($"action-consumer-unavailable request={request.GetType().Name}");
        }
        else
        {
            Failure = null;
        }

        return result == SequenceAcknowledgement.Applied;
    }

    /// <summary>Reports a changed diagnostic once rather than spamming each pending tick.</summary>
    /// <param name="diagnostic">Recoverable failure.</param>
    private void Fail(string diagnostic)
    {
        if (Failure != diagnostic)
        {
            Console.Error.WriteLine(diagnostic);
        }

        Failure = diagnostic;
    }
}
