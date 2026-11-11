using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>Renderer-backed action consumer. Asset families without a renderer remain explicitly unavailable.</summary>
internal sealed class BattleRendererSequencePlayback : IBattleSequencePlayback
{
    private readonly BattleRenderer _renderer;
    private readonly BattleFighterRoster _fighters;
    private ActionSequenceContext? _context;
    private Action<bool>? _finishAudio;

    /// <summary>Initializes a new instance of the <see cref="BattleRendererSequencePlayback"/> class.</summary>
    /// <param name="renderer">Owning renderer.</param>
    /// <param name="fighters">Physical roster, preserved throughout the action.</param>
    public BattleRendererSequencePlayback(BattleRenderer renderer, BattleFighterRoster fighters)
    {
        _renderer = renderer;
        _fighters = fighters;
    }

    /// <summary>Gets a value indicating whether a sequence exclusively owns the physical camera.</summary>
    public bool OwnsCamera => _context is not null;

    /// <inheritdoc/>
    public bool ChildrenReady => EffectChildrenReady?.Invoke() != false;

    /// <summary>Gets or sets the completion callback for children started by the asset consumer.</summary>
    public Func<bool>? EffectChildrenReady { get; set; }

    /// <summary>Gets or sets cleanup for children started by the asset consumer.</summary>
    public Action? CancelEffects { get; set; }

    /// <summary>Gets or sets the consumer for asset families owned by the effect/audio renderers.</summary>
    public Func<BattleSequenceEvent, SequenceAcknowledgement>? AssetConsumer { get; set; }

    /// <summary>Registers an audio consumer as the handler for <see cref="SoundRequestEvent"/> requests.
    /// Chains with any previously registered asset consumer.
    /// Also chains <see cref="CancelEffects"/> to stop all active voices when the action ends.</summary>
    /// <param name="audio">The audio consumer to register.</param>
    public void RegisterAudioConsumer(BattleAudioConsumer audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        RegisterAudioConsumer(audio.Consume, cancelled => audio.Finish(cancelled));
    }

    /// <summary>Registers the lazy production runtime using the same asset chaining seam.</summary>
    /// <param name="consume">Audio request consumer.</param>
    /// <param name="finish">Action-end or cancellation callback.</param>
    public void RegisterAudioConsumer(Func<BattleSequenceEvent, SequenceAcknowledgement> consume, Action<bool> finish)
    {
        var previous = AssetConsumer;
        AssetConsumer = request =>
        {
            SequenceAcknowledgement result = consume(request);
            if (result != SequenceAcknowledgement.Unavailable)
            {
                return result;
            }

            return previous?.Invoke(request) ?? SequenceAcknowledgement.Unavailable;
        };
        _finishAudio += finish;
    }

    /// <inheritdoc/>
    public void Begin(ActionSequenceContext context)
    {
        _renderer.StopShotCamera();
        _context = context;
    }

    /// <inheritdoc/>
    public bool MotionReady(int actor) => _context is { } context &&
        _fighters.SequenceActorModel(context.AttackerSide == BattleSide.Enemy, actor)?.Motion?.MotionDone == true;

    /// <inheritdoc/>
    public SequenceAcknowledgement Consume(BattleSequenceEvent request)
    {
        switch (request)
        {
            case ModelMotionEvent motion:
                return ApplyMotion(motion);
            case CameraRequestEvent camera:
                _renderer.OnSequenceCameraRequest(camera);
                return SequenceAcknowledgement.Applied;
            case HitDispatchedEvent hit when hit.DispatchIndex is >= 1 and <= 4:
            case SequenceEndedEvent:
                return SequenceAcknowledgement.Applied;
            default:
                return AssetConsumer?.Invoke(request) ?? SequenceAcknowledgement.Unavailable;
        }
    }

    /// <summary>Advances menu ownership only outside action playback.</summary>
    /// <param name="phase">Presented battle phase.</param>
    /// <param name="elapsed">Elapsed presentation seconds.</param>
    public void StepMenuCamera(BattleFlowPhase phase, float elapsed)
    {
        if (phase == BattleFlowPhase.PlayerCommand)
        {
            _renderer.StepShotCamera(elapsed);
        }
        else if (phase != BattleFlowPhase.ActionSequence)
        {
            _renderer.StopShotCamera();
        }
    }

    /// <summary>Applies a validated model request without interpreting motion zero as a new motion.</summary>
    /// <param name="request">Motion or visibility request.</param>
    /// <returns>Pending while the model or motion controller is unavailable, otherwise applied.</returns>
    public SequenceAcknowledgement ApplyMotion(ModelMotionEvent request)
    {
        var model = _fighters.SequenceActorModel(request.AttackerSide == BattleSide.Enemy, request.Actor);
        if (model is null || (request.Subcommand == 0 && model.Motion is null))
        {
            return SequenceAcknowledgement.Pending;
        }

        model.ApplySubcommand(request.Subcommand, request.Motion);
        return SequenceAcknowledgement.Applied;
    }

    /// <inheritdoc/>
    public void Finish(bool cancelled)
    {
        _finishAudio?.Invoke(cancelled);
        if (_context is null)
        {
            return;
        }

        CancelEffects?.Invoke();
        _context = null;
        _renderer.SetOpeningCamera();
        _fighters.RestoreVisibility();
    }
}
