using Dw3Production.Presentation;
using Dw3Production.Simulation;

namespace Dw3.Game.Tests;

/// <summary>Minimal playback stub that accepts model/hit/end events directly and delegates Sound requests
/// to a registered asset consumer chain. Used to exercise audio registration without Godot renderer objects.</summary>
internal sealed class AudioTestPlayback : IBattleSequencePlayback
{
    private bool _active;

    /// <summary>Gets or sets the asset consumer function registered by callers.</summary>
    public Func<BattleSequenceEvent, SequenceAcknowledgement>? AssetConsumer { get; set; }

    /// <summary>Gets or sets the cancellation action registered by callers.</summary>
    public Action? CancelEffects { get; set; }

    /// <summary>Gets or sets a value indicating whether children are ready.</summary>
    public bool ChildrenReady { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether model motion is ready.</summary>
    public bool Ready { get; set; }

    /// <summary>Gets the list of non-Sound events directly accepted by this stub.</summary>
    public List<BattleSequenceEvent> Applied { get; } = [];

    /// <summary>Gets the finish count.</summary>
    public int Finishes { get; private set; }

    /// <inheritdoc/>
    public void Begin(ActionSequenceContext context) => _active = true;

    /// <inheritdoc/>
    public bool MotionReady(int actor) => Ready;

    /// <inheritdoc/>
    public SequenceAcknowledgement Consume(BattleSequenceEvent request)
    {
        ArgumentNullException.ThrowIfNull(request);
        switch (request)
        {
            case SoundRequestEvent:
                return AssetConsumer?.Invoke(request) ?? SequenceAcknowledgement.Unavailable;
            case ModelMotionEvent:
            case HitDispatchedEvent:
            case SequenceEndedEvent:
                Applied.Add(request);
                return SequenceAcknowledgement.Applied;
            default:
                return AssetConsumer?.Invoke(request) ?? SequenceAcknowledgement.Unavailable;
        }
    }

    /// <inheritdoc/>
    public void Finish(bool cancelled)
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        Finishes++;
        CancelEffects?.Invoke();
    }

    /// <summary>Registers an audio consumer by wiring AssetConsumer and CancelEffects,
    /// mirroring the production wiring in BattleRendererSequencePlayback.</summary>
    /// <param name="audio">Consumer to register.</param>
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
}
