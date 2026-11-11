using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>Renderer-backed action consumer. Asset families without a renderer remain explicitly unavailable.</summary>
internal sealed class BattleRendererSequencePlayback : IBattleSequencePlayback
{
    private readonly BattleRenderer _renderer;
    private readonly BattleFighterRoster _fighters;
    private readonly Dictionary<BattleModel, BattleJumpMotion> _jumps = [];
    private readonly Dictionary<BattleModel, (Godot.Vector3 Position, int Motion, int Idle)> _homes = [];
    private readonly Dictionary<BattleModel, BattleTravelMotion> _moves = [];
    private ActionSequenceContext? _context;
    private BattleReactionPlayback? _reaction;
    private BattleImpactPlayback? _impact;

    /// <summary>Initializes a new instance of the <see cref="BattleRendererSequencePlayback"/> class.</summary>
    /// <param name="renderer">Owning renderer.</param>
    /// <param name="fighters">Physical roster, preserved throughout the action.</param>
    public BattleRendererSequencePlayback(BattleRenderer renderer, BattleFighterRoster fighters)
    {
        _renderer = renderer;
        _fighters = fighters;
    }

    /// <summary>Gets the private diagnostic roster.</summary>
    public BattleFighterRoster DiagnosticRoster => _fighters;

    /// <summary>Gets a value indicating whether a sequence exclusively owns the physical camera.</summary>
    public bool OwnsCamera => _context is not null;

    /// <inheritdoc/>
    public bool ChildrenReady => EffectChildrenReady?.Invoke() != false && _reaction?.IsComplete != false && _impact?.IsComplete != false && _jumps.Values.All(jump => jump.IsComplete);

    /// <summary>Gets or sets the completion callback for children started by the asset consumer.</summary>
    public Func<bool>? EffectChildrenReady { get; set; }

    /// <summary>Gets or sets cleanup for children started by the asset consumer.</summary>
    public Action? CancelEffects { get; set; }

    /// <summary>Gets or sets the consumer for asset families owned by the effect/audio renderers.</summary>
    public Func<BattleSequenceEvent, SequenceAcknowledgement>? AssetConsumer { get; set; }

    /// <summary>Binds the same pack provider used by the parent runner.</summary>
    /// <param name="scripts">Validated pack-backed action scripts.</param>
    public void ConfigureReactions(IActionScriptProvider scripts) => _reaction = new BattleReactionPlayback(scripts, this);

    /// <summary>Registers the renderer-owned contact effect consumer without replacing other families.</summary>
    /// <param name="impact">Contact consumer bound to the same validated pack.</param>
    public void ConfigureImpact(BattleImpactPlayback impact)
    {
        _impact?.Clear();
        _impact = impact;
    }

    /// <summary>Registers an audio consumer as the handler for <see cref="SoundRequestEvent"/> requests.
    /// Chains with any previously registered asset consumer.
    /// Also chains <see cref="CancelEffects"/> to stop all active voices when the action ends.</summary>
    /// <param name="audio">The audio consumer to register.</param>
    public void RegisterAudioConsumer(BattleAudioConsumer audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        var previous = AssetConsumer;
        AssetConsumer = request =>
        {
            SequenceAcknowledgement result = audio.Consume(request);
            if (result != SequenceAcknowledgement.Unavailable)
            {
                return result;
            }

            return previous?.Invoke(request) ?? SequenceAcknowledgement.Unavailable;
        };
        var previousCancel = CancelEffects;
        CancelEffects = () =>
        {
            audio.StopAll();
            previousCancel?.Invoke();
        };
    }

    /// <inheritdoc/>
    public void Begin(ActionSequenceContext context)
    {
        _renderer.StopShotCamera();
        _context = context;
        _impact?.Begin(context);
        foreach (var model in _fighters.Models)
        {
            _homes[model] = (model.Root.Position, model.Motion?.Motion ?? 1, model.Motion?.IdleMotion ?? 0);
        }
    }

    /// <inheritdoc/>
    public bool MotionReady(int actor)
        => MotionReady(_context?.AttackerSide ?? BattleSide.Player, actor);

    /// <summary>Polls physical motion for the parent or reaction side.</summary>
    /// <param name="side">Owning script side.</param>
    /// <param name="actor">Source actor selector.</param>
    /// <returns>Whether the selected motion completed.</returns>
    public bool MotionReady(BattleSide side, int actor)
    {
        var model = _fighters.SequenceActorModel(side == BattleSide.Enemy, actor);
        return model?.Motion?.MotionDone == true;
    }

    /// <inheritdoc/>
    public bool JumpReady(int actor)
        => JumpReady(_context?.AttackerSide ?? BattleSide.Player, actor);

    /// <summary>Polls independent jump completion for either script side.</summary>
    /// <param name="side">Owning script side.</param>
    /// <param name="actor">Source actor selector.</param>
    /// <returns>Whether the selected jump completed.</returns>
    public bool JumpReady(BattleSide side, int actor)
    {
        var model = _fighters.SequenceActorModel(side == BattleSide.Enemy, actor);
        return model is not null && (!_jumps.TryGetValue(model, out var jump) || jump.IsComplete);
    }

    /// <inheritdoc/>
    public SequenceAcknowledgement Consume(BattleSequenceEvent request)
    {
        switch (request)
        {
            case StageChangeEvent stage:
                return ConsumeImpact(stage);
            case SpriteEffectEvent sprite:
                return ConsumeImpact(sprite);
            case ModelMotionEvent motion:
                return ApplyMotion(motion);
            case ModelJumpEvent jump:
                return ApplyJump(jump);
            case ModelTravelEvent travel:
                return ApplyTravel(travel);
            case CameraRequestEvent camera:
                _renderer.OnSequenceCameraRequest(camera);
                return SequenceAcknowledgement.Applied;
            case HitDispatchedEvent hit when hit.DispatchIndex is >= 1 and <= 4:
            case SequenceEndedEvent:
                return SequenceAcknowledgement.Applied;
            case HitDispatchedEvent hit:
                return _reaction?.Begin(hit) ?? SequenceAcknowledgement.Unavailable;
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

    /// <summary>Advances physical children by one normalized source-time sample on the retained presentation clock.</summary>
    public void StepPhysical()
    {
        _reaction?.Step();
        _impact?.Step();
        foreach (var pair in _moves)
        {
            if (!pair.Value.IsComplete)
            {
                pair.Value.Step();
                pair.Key.Root.Position = pair.Value.Position;
            }
        }

        foreach (var pair in _jumps)
        {
            if (!pair.Value.IsComplete)
            {
                pair.Value.Step();
                pair.Key.Root.Position = pair.Value.Position;
            }
        }
    }

    /// <inheritdoc/>
    public void Finish(bool cancelled)
    {
        if (_context is null)
        {
            return;
        }

        CancelEffects?.Invoke();
        _reaction?.Clear();
        _impact?.Clear();
        foreach (var pair in _homes)
        {
            pair.Key.Root.Position = pair.Value.Position;
            if (cancelled)
            {
                pair.Key.SetMotion(pair.Value.Motion, restart: true);
                pair.Key.Motion?.SetIdleMotion(pair.Value.Idle);
            }
        }

        _homes.Clear();
        _jumps.Clear();
        _moves.Clear();
        _context = null;
        _renderer.SetOpeningCamera();
        _fighters.RestoreVisibility();
    }

    /// <summary>Chains contact requests without swallowing other registered effect families.</summary>
    /// <param name="request">Stable request identity.</param>
    /// <returns>The first available consumer acknowledgement.</returns>
    private SequenceAcknowledgement ConsumeImpact(BattleSequenceEvent request)
    {
        var result = _impact?.Consume(request) ?? SequenceAcknowledgement.Unavailable;
        return result == SequenceAcknowledgement.Unavailable
            ? AssetConsumer?.Invoke(request) ?? SequenceAcknowledgement.Unavailable : result;
    }

    /// <summary>Applies absolute travel, home travel or the authored near-contact placement.</summary>
    /// <param name="request">Validated source travel operands.</param>
    /// <returns>Pending until the physical actor exists.</returns>
    private SequenceAcknowledgement ApplyTravel(ModelTravelEvent request)
    {
        var a = request.Arguments;
        var model = _fighters.SequenceActorModel(request.AttackerSide == BattleSide.Enemy, a[1]);
        if (model is null)
        {
            return SequenceAcknowledgement.Pending;
        }

        var target = _homes[model].Position;
        if (a[0] == 3)
        {
            target = new Godot.Vector3(a[2], a[3], a[4]) / BattleSceneMath.Ps1UnitsPerGodotUnit;
        }
        else if (a[0] == 8)
        {
            target.Z += (model == _fighters.Enemy ? 1 : -1) * 0x2800 / BattleSceneMath.Ps1UnitsPerGodotUnit;
        }

        var move = new BattleTravelMotion(model.Root.Position, target, a[^1]);
        _moves[model] = move;
        model.Root.Position = move.Position;
        return SequenceAcknowledgement.Applied;
    }

    /// <summary>Starts the qualified kind-four jump on the requested physical actor.</summary>
    /// <param name="request">Jump request with an optional kind-four distance.</param>
    /// <returns>Pending without a physical model, otherwise applied.</returns>
    private SequenceAcknowledgement ApplyJump(ModelJumpEvent request)
    {
        var model = _fighters.SequenceActorModel(request.AttackerSide == BattleSide.Enemy, request.Actor);
        if (model is null)
        {
            return SequenceAcknowledgement.Pending;
        }

        try
        {
            _jumps[model] = new BattleJumpMotion(_homes[model].Position, request.Kind, request.Distance, model == _fighters.Enemy, model.Root.Position);
            return SequenceAcknowledgement.Applied;
        }
        catch (ArgumentOutOfRangeException)
        {
            return SequenceAcknowledgement.Unavailable;
        }
    }
}
