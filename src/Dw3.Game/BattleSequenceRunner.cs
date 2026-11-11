using Dw3.Data.Battle;
using Dw3Production.Conversion;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;

namespace Dw3Production;

/// <summary>Manages the action-sequence lifecycle for one battle, loading scripts from the pack
/// and advancing the timeline frame by frame. Isolated from <see cref="GameHost"/> so its branching
/// logic is counted separately for DW3001.</summary>
internal sealed class BattleSequenceRunner
{
    private readonly BattleFlowController _flow;
    private readonly IActionScriptProvider _scripts;
    private BattleActionTimeline? _timeline;
    private BattleRenderer? _renderer;

    /// <summary>Initializes a new instance of the <see cref="BattleSequenceRunner"/> class.</summary>
    /// <param name="flow">Active battle flow whose sequence context and phases are driven.</param>
    /// <param name="scripts">Provider that loads decoded action scripts on demand.</param>
    public BattleSequenceRunner(BattleFlowController flow, IActionScriptProvider scripts)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(scripts);
        _flow = flow;
        _scripts = scripts;
    }

    /// <summary>Binds or clears the renderer that receives sequence events each frame.</summary>
    /// <param name="renderer">Active renderer, or null when the battle renderer is not set up.</param>
    public void SetRenderer(BattleRenderer? renderer) => _renderer = renderer;

    /// <summary>Advances the sequence by one frame: loads the script on the first frame, fires events,
    /// and completes the flow phase when the End opcode is reached.</summary>
    public void Tick()
    {
        if (_timeline is null && !TryStartTimeline())
        {
            return;
        }

        if (_timeline is not { } current)
        {
            return;
        }

        var events = current.Advance(1);
        RouteEvents(events);
        if (current.IsComplete)
        {
            _timeline = null;
            _flow.CompleteActionSequence();
        }
    }

    /// <summary>Routes one event to the renderer, delegating to the right handler per event type.
    /// Static so it carries no implicit complexity for the class-level DW3001 count.</summary>
    /// <param name="renderer">Active renderer.</param>
    /// <param name="ev">Typed event from the timeline.</param>
    private static void RouteOneEvent(BattleRenderer renderer, BattleSequenceEvent ev)
    {
        switch (ev)
        {
            case HitDispatchedEvent hit:
                renderer.OnHitDispatched(hit);
                break;
            case ModelMotionEvent motion:
                renderer.OnSequenceMotion(motion);
                break;
            case CameraRequestEvent camera:
                renderer.OnSequenceCameraRequest(camera);
                break;
            default:
                break;
        }
    }

    /// <summary>Loads the pending context and builds the timeline on the first sequence frame.
    /// Returns false when no script is available and the sequence has already been completed.</summary>
    /// <returns>True when the timeline is ready for advancement.</returns>
    private bool TryStartTimeline()
    {
        var ctx = _flow.ConsumeSequenceContext();
        if (ctx is null)
        {
            _flow.CompleteActionSequence();
            return false;
        }

        var script = LoadScript(ctx.AttackerId, ctx.ScriptSelector);
        if (script is null)
        {
            _flow.CompleteActionSequence();
            return false;
        }

        _timeline = new BattleActionTimeline(script, ctx);
        return true;
    }

    /// <summary>Loads the action script from the provider, returning null on cache miss or error.</summary>
    /// <param name="fighterId">Fighter whose script is requested.</param>
    /// <param name="selector">Technique script-selector.</param>
    /// <returns>The decoded script, or null.</returns>
    private BattleActionScript? LoadScript(int fighterId, int selector)
    {
        try
        {
            return _scripts.TryLoad(fighterId, selector);
        }
        catch (Exception ex)
        {
            QuietLog.Info($"action-script-load-error fighter={fighterId} sel={selector}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Routes typed events to the renderer for supported families; silently skips others.</summary>
    /// <param name="events">Events from the timeline this frame.</param>
    private void RouteEvents(IReadOnlyList<BattleSequenceEvent> events)
    {
        if (_renderer is null)
        {
            return;
        }

        foreach (var ev in events)
        {
            RouteOneEvent(_renderer, ev);
        }
    }
}
