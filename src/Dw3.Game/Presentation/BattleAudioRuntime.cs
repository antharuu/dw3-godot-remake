using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Renderer-owned lazy pack consumer and physical audio lifetime, shared with GameHost.</summary>
internal sealed class BattleAudioRuntime : IDisposable
{
    private readonly GodotBattleAudioBackend _backend;
    private readonly string _dataRoot;
    private BattleAudioConsumer? _consumer;

    /// <summary>Initializes a new instance of the <see cref="BattleAudioRuntime"/> class.</summary>
    /// <param name="root">Physical players' owner.</param>
    /// <param name="dataRoot">User-prepared pack directory.</param>
    public BattleAudioRuntime(Node root, string dataRoot)
    {
        _backend = new GodotBattleAudioBackend(root);
        _dataRoot = dataRoot;
    }

    /// <summary>Gets allocated physical channels, including trailing natural notes.</summary>
    public int ActiveCount => _backend.ActiveCount;

    /// <summary>Loads only when needed so missing audio is diagnosed at the required action.</summary>
    /// <param name="request">Stable sound request or another asset family.</param>
    /// <returns>Explicit consumer acknowledgement.</returns>
    public SequenceAcknowledgement Consume(BattleSequenceEvent request)
    {
        if (request is not SoundRequestEvent)
        {
            return SequenceAcknowledgement.Unavailable;
        }

        _consumer ??= new BattleAudioConsumer(BattleActionAudioLoader.Load(new DataPackReader(_dataRoot)), _backend);
        return _consumer.Consume(request);
    }

    /// <summary>Ends a battle or cancels its voices without requiring a replacement backend.</summary>
    public void StopAll()
    {
        _consumer?.StopAll();
        _backend.StopAll();
    }

    /// <summary>Does not cut naturally decaying notes at script End.</summary>
    /// <param name="cancelled">Whether the action was interrupted.</param>
    public void Finish(bool cancelled)
    {
        _consumer?.Finish(cancelled);
        if (cancelled)
        {
            _backend.StopAll();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        StopAll();
        _backend.Dispose();
    }
}
