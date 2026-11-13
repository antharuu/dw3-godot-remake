using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Renderer-owned lazy pack consumer and physical audio lifetime, shared with GameHost.</summary>
internal sealed class BattleAudioRuntime : IDisposable
{
    /// <summary>Source sequence fade-out: one second, 60 nominal units.</summary>
    private const int MusicFadeOutTicks = 60;

    private readonly GodotBattleAudioBackend _backend;
    private readonly string _dataRoot;
    private readonly BattleAudioPump _sequencePump;
    private readonly BattleEntranceAudio _entrance;
    private readonly Dictionary<int, PreparedAudioSequence> _sequences = [];
    private BattleAudioConsumer? _consumer;
    private BattleMusicPlayback? _music;
    private uint _musicId;
    private BattleMusicPlayback? _victory;

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

    /// <summary>Gets how many entrance cues played.</summary>
    public int EntranceCuePlays => _entrance.Plays;

    /// <summary>Gets the playing battle music and its fade-out volume, zero when no music plays.</summary>
    public (uint Id, float Volume) MusicState => _music is { } music ? (_musicId, music.Volume) : (0u, 0f);

    /// <summary>Gets the completed boundaries of the current battle-music track.</summary>
    public int MusicLoopCount => _music?.LoopCount ?? 0;

    /// <summary>Gets how many victory tracks this runtime actually started, for single-occurrence evidence.</summary>
    public int VictoryStarts { get; private set; }

    /// <summary>Gets the physical global reverb state as source type and depth.</summary>
    public (int Type, int Depth) Reverb => (_backend.Reverb.Type, _backend.Reverb.Depth);

    /// <summary>Arms the prepared direct cue after incoming-model readiness, or plays it at once when the caller
    /// already counted the lead-in on its own clock (an in-battle entrance), so the delay is never applied twice.</summary>
    /// <param name="immediate">Whether to play the cue now instead of after its prepared delay.</param>
    public void BeginEntrance(bool immediate = false)
    {
        var cue = BattleEntranceAudioLoader.Load(new DataPackReader(_dataRoot));
        _backend.Prepare(cue.Entry);
        _sequencePump.Synchronize();
        int delay = immediate ? 0 : cue.Delay;
        _entrance.Begin(delay, cue.Entry);
        GD.Print($"battle-entrance-audio-armed id={cue.Entry.EncodedId:X8} delay={delay}");
        if (immediate)
        {
            _entrance.Advance(0);
        }
    }

    /// <summary>Starts the selected exclusive battle track and stops any prior battle music.</summary>
    /// <param name="musicId">Encoded source music identity from the encounter.</param>
    public void BeginMusic(uint musicId)
    {
        StopMusic();
        var music = BattleSequenceAudioLoader.LoadMusic(new DataPackReader(_dataRoot), musicId)
            ?? throw new FormatException($"Battle music {musicId:X8} is unavailable.");
        Prepare(music);
        _music = new BattleMusicPlayback(music, _backend);
        _musicId = musicId;
        _sequencePump.Synchronize();
        GD.Print($"battle-music-start id={musicId:X8} loop={music.LoopSeconds:0.000}");
    }

    /// <summary>Fades the battle music out over one second when it is the given track, as the source's
    /// <c>SOUND.fadeOut</c> only acts on a loaded bank; the silent track then stops.</summary>
    /// <param name="musicId">Encoded source music identity to fade out.</param>
    public void FadeOutMusic(uint musicId)
    {
        if (_music is { } music && _musicId == musicId)
        {
            music.FadeOut(MusicFadeOutTicks);
            GD.Print($"battle-music-fade-out id={musicId:X8} ticks={MusicFadeOutTicks}");
        }
    }

    /// <summary>
    /// Transitions a won battle from its looping music to the source victory track. The track carries
    /// its own authored endless loop (fanfare, then a repeated body), so it uses the same loop owner.
    /// </summary>
    public void BeginVictory()
    {
        StopMusic();
        if (_victory is not null)
        {
            return;
        }

        const uint victoryJingle = 0x6004001E;
        var sequence = BattleSequenceAudioLoader.LoadMusic(new DataPackReader(_dataRoot), victoryJingle)
            ?? throw new FormatException("Battle victory jingle is unavailable.");
        Prepare(sequence);
        _victory = new BattleMusicPlayback(sequence, _backend);
        VictoryStarts++;
        _sequencePump.Synchronize();
        GD.Print($"battle-victory-audio-start id={victoryJingle:X8}");
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
        StopMusic();
        _victory?.Stop();
        _victory = null;
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
        _music?.Advance(ticks);
        if (_music?.IsSilent == true)
        {
            StopMusic();
        }

        _victory?.Advance(ticks);
    }

    /// <summary>Stops the currently repeating battle track.</summary>
    private void StopMusic()
    {
        _music?.Stop();
        _music = null;
    }

    /// <summary>Primes every referenced sample before a sequence starts.</summary>
    /// <param name="sequence">Prepared sequence whose samples are required.</param>
    private void Prepare(PreparedAudioSequence sequence)
    {
        foreach (var entry in sequence.Commands.Select(command => command.Entry).OfType<AudioProgramEntry>())
        {
            _backend.Prepare(entry);
        }
    }
}
