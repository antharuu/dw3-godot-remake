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
    private readonly BattleAudioPump _sequencePump;
    private readonly BattleEntranceAudio _entrance;
    private readonly Dictionary<int, PreparedAudioSequence> _sequences = [];
    private BattleAudioConsumer? _consumer;

    /// <summary>Initializes a new instance of the <see cref="BattleAudioRuntime"/> class.</summary>
    /// <param name="root">Physical players' owner.</param>
    /// <param name="dataRoot">User-prepared pack directory.</param>
    public BattleAudioRuntime(Node root, string dataRoot)
    {
        _backend = new GodotBattleAudioBackend(root);
        _dataRoot = dataRoot;
        _entrance = new BattleEntranceAudio(entry => _backend.Play(entry));
        _sequencePump = new BattleAudioPump { Advance = Advance };
        root.AddChild(_sequencePump);
    }

    /// <summary>Gets allocated physical channels, including trailing natural notes.</summary>
    public int ActiveCount => _backend.ActiveCount;

    /// <summary>Arms the prepared direct cue after incoming-model readiness.</summary>
    public void BeginEntrance()
    {
        var cue = BattleEntranceAudioLoader.Load(new DataPackReader(_dataRoot));
        _backend.Prepare(cue.Entry);
        _sequencePump.Synchronize();
        _entrance.Begin(cue.Delay, cue.Entry);
        GD.Print($"battle-entrance-audio-armed id={cue.Entry.EncodedId:X8} delay={cue.Delay}");
    }

    /// <summary>Loads only when needed so missing audio is diagnosed at the required action.</summary>
    /// <param name="request">Stable sound request or another asset family.</param>
    /// <returns>Explicit consumer acknowledgement.</returns>
    public SequenceAcknowledgement Consume(BattleSequenceEvent request)
    {
        if (request is not SoundRequestEvent sound)
        {
            return SequenceAcknowledgement.Unavailable;
        }

        var pack = new DataPackReader(_dataRoot);
        _consumer ??= new BattleAudioConsumer(BattleActionAudioLoader.Load(pack), _backend, _sequences);
        _sequencePump.Synchronize();
        var result = _consumer.Consume(request);
        if (result == SequenceAcknowledgement.Unavailable && BattleSequenceAudioLoader.Load(pack, sound.SoundId) is { } sequence)
        {
            foreach (var entry in sequence.Commands.Select(command => command.Entry).OfType<AudioProgramEntry>())
            {
                _backend.Prepare(entry);
            }

            _sequences.Add(sound.SoundId, sequence);
            _sequencePump.Synchronize();
            result = _consumer.Consume(request);
        }

        return result;
    }

    /// <summary>Ends a battle or cancels its voices without requiring a replacement backend.</summary>
    public void StopAll()
    {
        _entrance.Cancel();
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
            _entrance.Cancel();
            _backend.StopAll();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        StopAll();
        _backend.Dispose();
        _sequencePump.Advance = null;
        _sequencePump.QueueFree();
    }

    /// <summary>Drives sequence and entrance deadlines on the same source clock.</summary>
    /// <param name="ticks">Elapsed nominal 60 Hz units.</param>
    private void Advance(int ticks)
    {
        _consumer?.Advance(ticks);
        _entrance.Advance(ticks);
    }
}
