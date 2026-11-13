using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>One independently started occurrence of a prepared sequence.</summary>
internal sealed class BattleSequenceVoice
{
    /// <summary>Unsigned 14-bit MIDI bend centre, the state of a freshly opened sequence.</summary>
    private const int CentreBend = 8192;

    private readonly PreparedAudioSequence _sequence;
    private readonly IBattleAudioBackend _backend;
    private readonly List<(int Channel, int Note, AudioProgramEntry Entry, IVoiceHandle Handle)> _voices = [];
    private readonly int[] _pitchBends;
    private double _seconds;
    private int _index;
    private bool _ended;

    /// <summary>Initializes a new instance of the <see cref="BattleSequenceVoice"/> class.</summary>
    /// <param name="sequence">Validated prepared transitions.</param>
    /// <param name="backend">Shared physical voice allocator.</param>
    /// <param name="pitchBends">Channel bend state continued from a previous pass, or null for the MIDI centre.</param>
    public BattleSequenceVoice(PreparedAudioSequence sequence, IBattleAudioBackend backend, IReadOnlyList<int>? pitchBends = null)
    {
        _sequence = sequence;
        _backend = backend;
        _pitchBends = pitchBends is null ? [.. Enumerable.Repeat(CentreBend, 16)] : [.. pitchBends];
        if (_pitchBends.Length != 16)
        {
            throw new ArgumentException("Sixteen channel bends are required.", nameof(pitchBends));
        }

        Advance(0);
    }

    /// <summary>Gets the current channel bend state, which libsnd keeps across an authored loop jump.</summary>
    public IReadOnlyList<int> PitchBends => _pitchBends;

    /// <summary>Gets a value indicating whether the authored event schedule reached its end marker.</summary>
    public bool HasReachedEnd => _seconds >= _sequence.EndSeconds;

    /// <summary>Gets a value indicating whether all events and voices have ended.</summary>
    public bool IsComplete => HasReachedEnd && _voices.Count == 0;

    /// <summary>Advances all due transitions on the source clock without rescaling tick deltas.</summary>
    /// <param name="ticks">Elapsed nominal 60 Hz units.</param>
    public void Advance(int ticks)
    {
        _seconds += ticks / 60.0;
        while (_index < _sequence.Commands.Count && _sequence.Commands[_index].Seconds <= _seconds + 1e-9)
        {
            Apply(_sequence.Commands[_index++]);
        }

        _voices.RemoveAll(voice => voice.Handle.IsComplete);
        if (_seconds >= _sequence.EndSeconds && !_ended)
        {
            _ended = true;
            foreach (var voice in _voices.ToArray())
            {
                Release(voice);
            }
        }
    }

    /// <summary>Interrupts scheduled and active notes, including repeating samples.</summary>
    public void StopAll()
    {
        foreach (var voice in _voices)
        {
            _backend.StopVoice(voice.Handle);
        }

        _voices.Clear();
        _index = _sequence.Commands.Count;
        _seconds = _sequence.EndSeconds;
        _ended = true;
    }

    /// <summary>Maps the source 14-bit centre and asymmetrical VAB bounds to semitones.</summary>
    /// <param name="entry">Tone attributes owning the bend bounds.</param>
    /// <param name="bend">Unsigned source 14-bit bend value.</param>
    /// <returns>Signed physical pitch offset in semitones.</returns>
    private static float BendSemitones(AudioProgramEntry entry, int bend) =>
        bend >= 8192 ? (bend - 8192) * entry.PitchBendUp / 8191f : (bend - 8192) * entry.PitchBendDown / 8192f;

    /// <summary>Applies one source note transition through the real shared backend.</summary>
    /// <param name="command">Resolved event.</param>
    private void Apply(AudioSequenceCommand command)
    {
        if (command.PitchBend is { } bend)
        {
            ApplyPitchBend(command.Channel, bend);
            return;
        }

        if (command.Reverb is { } reverb)
        {
            _backend.ApplyReverb(reverb);
            return;
        }

        if (command.Entry is { } entry)
        {
            var handle = _backend.Play(entry);
            handle.SetPitchBend(BendSemitones(entry, _pitchBends[command.Channel]));
            _voices.Add((command.Channel, command.Note, entry, handle));
            return;
        }

        foreach (var voice in _voices.Where(voice => voice.Channel == command.Channel && voice.Note == command.Note).ToArray())
        {
            Release(voice);
        }
    }

    /// <summary>Updates active source-channel voices and retains a bend for future note-on commands.</summary>
    /// <param name="channel">Source MIDI channel.</param>
    /// <param name="bend">Unsigned source 14-bit bend value.</param>
    private void ApplyPitchBend(int channel, int bend)
    {
        _pitchBends[channel] = bend;
        foreach (var voice in _voices.Where(voice => voice.Channel == channel))
        {
            voice.Handle.SetPitchBend(BendSemitones(voice.Entry, bend));
        }
    }

    /// <summary>Keys off a note while retaining its physical release tail for cancellation.</summary>
    /// <param name="voice">Tracked physical note identity.</param>
    private void Release((int Channel, int Note, AudioProgramEntry Entry, IVoiceHandle Handle) voice)
    {
        if (!voice.Handle.KeyOff())
        {
            _backend.StopVoice(voice.Handle);
            _voices.Remove(voice);
        }
    }
}
