using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Plays prepared PCM through the engine mixer. Never opens the source disc.</summary>
internal sealed class GodotBattleAudioBackend : IBattleAudioBackend, IDisposable
{
    private readonly Node _root;
    private readonly BattleAudioVoicePool _pool;
    private readonly BattleAudioPump _pump;
    private readonly Dictionary<string, AudioStreamWav> _samples = new(StringComparer.Ordinal);
    private readonly GodotReverbBus _reverb = new();

    /// <summary>Initializes a new instance of the <see cref="GodotBattleAudioBackend"/> class.</summary>
    /// <param name="root">Node owning physical audio players.</param>
    public GodotBattleAudioBackend(Node root)
    {
        _root = root;
        _pool = new BattleAudioVoicePool(Start, handle => ((GodotVoiceHandle)handle).Stop());
        _pump = new BattleAudioPump { Advance = _pool.Advance };
        root.AddChild(_pump);
    }

    /// <summary>Gets current physical channel usage for lifecycle diagnostics.</summary>
    public int ActiveCount => _pool.ActiveCount;

    /// <summary>Gets the physical global reverb consumer, for diagnostics.</summary>
    public GodotReverbBus Reverb => _reverb;

    /// <inheritdoc/>
    public void Prepare(AudioProgramEntry entry)
    {
        if (!_samples.TryGetValue(entry.WavPath, out var stream))
        {
            stream = AudioStreamWav.LoadFromFile(entry.WavPath) ?? throw new IOException($"Invalid battle WAV: {entry.WavPath}");
            if (stream.Format != AudioStreamWav.FormatEnum.Format16Bits || stream.Stereo || stream.Data.Length == 0)
            {
                stream.Dispose();
                throw new FormatException($"Expected nonempty prepared mono PCM16: {entry.WavPath}");
            }

            _samples.Add(entry.WavPath, stream);
        }

        if (entry.Loops && (entry.LoopStart < 0 || entry.LoopStart >= stream.Data.Length / 2))
        {
            throw new FormatException("Battle sample has an invalid loop marker.");
        }
    }

    /// <inheritdoc/>
    public IVoiceHandle Play(AudioProgramEntry entry)
    {
        _pump.Synchronize();
        return _pool.Play(entry);
    }

    /// <inheritdoc/>
    public void StopVoice(IVoiceHandle handle) => _pool.StopVoice(handle);

    /// <inheritdoc/>
    public void ApplyReverb(AudioReverbChange change) => _reverb.Apply(change);

    /// <summary>Ends a battle without replacing this backend. The global reverb keeps its state, so a tail drains naturally.</summary>
    public void StopAll() => _pool.StopAll();

    /// <inheritdoc/>
    public void Dispose()
    {
        StopAll();
        _pump.Advance = null;
        _pump.QueueFree();
        foreach (var sample in _samples.Values)
        {
            sample.Dispose();
        }

        _samples.Clear();
        _reverb.Dispose();
    }

    /// <summary>Creates a real mixer player with decoded loop, gain, pan and pitch attributes.</summary>
    /// <param name="entry">Resolved sound and sample.</param>
    /// <returns>Physical voice, not an acknowledgement fake.</returns>
    private GodotVoiceHandle Start(AudioProgramEntry entry)
    {
        Prepare(entry);
        _pump.Synchronize();
        var stream = (AudioStreamWav)_samples[entry.WavPath].Duplicate();
        stream.Data = BattleAudioPcm.Pan(stream.Data, ((entry.Pan - 64) / 64f) + entry.PanOffset);
        stream.Stereo = true;
        stream.LoopMode = entry.Loops ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
        stream.LoopBegin = entry.LoopStart;
        stream.LoopEnd = stream.Data.Length / 4;
        var player = new AudioStreamPlayer
        {
            Stream = stream,
            VolumeLinear = entry.Volume / 127f * entry.BankGain,
            PitchScale = MathF.Pow(2, (entry.Note - entry.CenterNote - (entry.PitchShift / 128f)) / 12f),
            Bus = entry.Reverb ? _reverb.Name : "Master",
        };
        _root.AddChild(player);
        var voice = new GodotVoiceHandle(player, stream, entry);
        GD.Print($"battle-audio-start id={entry.EncodedId:X8} program={entry.Program} tone={entry.Tone} sample={entry.SampleIndex} note={entry.Note} pitch={player.PitchScale} gain={player.VolumeLinear} pan={entry.Pan} priority={entry.Priority} loop={entry.Loops} loopStart={entry.LoopStart} duration={entry.DurationFrames} reverb={entry.Reverb}");
        player.Finished += voice.Stop;
        player.Play();
        return voice;
    }
}
