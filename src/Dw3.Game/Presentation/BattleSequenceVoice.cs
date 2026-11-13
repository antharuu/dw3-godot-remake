using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>One independently started occurrence of a prepared sequence.</summary>
internal sealed class BattleSequenceVoice
{
    private readonly PreparedAudioSequence _sequence;
    private readonly IBattleAudioBackend _backend;
    private readonly List<(int Channel, int Note, IVoiceHandle Handle)> _voices = [];
    private double _seconds;
    private int _index;
    private bool _ended;

    /// <summary>Initializes a new instance of the <see cref="BattleSequenceVoice"/> class.</summary>
    /// <param name="sequence">Validated prepared transitions.</param>
    /// <param name="backend">Shared physical voice allocator.</param>
    public BattleSequenceVoice(PreparedAudioSequence sequence, IBattleAudioBackend backend)
    {
        _sequence = sequence;
        _backend = backend;
        Advance(0);
    }

    /// <summary>Gets a value indicating whether all events and voices have ended.</summary>
    public bool IsComplete => _seconds >= _sequence.EndSeconds && _voices.Count == 0;

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

    /// <summary>Applies one source note transition through the real shared backend.</summary>
    /// <param name="command">Resolved event.</param>
    private void Apply(AudioSequenceCommand command)
    {
        if (command.Entry is { } entry)
        {
            _voices.Add((command.Channel, command.Note, _backend.Play(entry)));
            return;
        }

        foreach (var voice in _voices.Where(voice => voice.Channel == command.Channel && voice.Note == command.Note).ToArray())
        {
            Release(voice);
        }
    }

    /// <summary>Keys off a note while retaining its physical release tail for cancellation.</summary>
    /// <param name="voice">Tracked physical note identity.</param>
    private void Release((int Channel, int Note, IVoiceHandle Handle) voice)
    {
        if (!voice.Handle.KeyOff())
        {
            _backend.StopVoice(voice.Handle);
            _voices.Remove(voice);
        }
    }
}
