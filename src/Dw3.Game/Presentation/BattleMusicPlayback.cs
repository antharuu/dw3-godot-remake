using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>
/// Owns one looping prepared battle track and no unrelated action voices. The track plays from its
/// start to the authored loop end (CC99=30, or the end of track when absent), then jumps back to the
/// authored loop start (CC99=20) for ever, the endless count being validated by the compiler. Channel
/// bends carry across the jump as in libsnd, and the global reverb lives in the backend, not here.
/// </summary>
internal sealed class BattleMusicPlayback
{
    private readonly PreparedAudioSequence _loop;
    private readonly IBattleAudioBackend _backend;
    private BattleSequenceVoice _voice;
    private bool _looping;

    /// <summary>Initializes a new instance of the <see cref="BattleMusicPlayback"/> class.</summary>
    /// <param name="sequence">Prepared source track with a required loop marker.</param>
    /// <param name="backend">Shared physical battle-audio backend.</param>
    public BattleMusicPlayback(PreparedAudioSequence sequence, IBattleAudioBackend backend)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(backend);
        double loopEnd = sequence.LoopEndSeconds ?? sequence.EndSeconds;
        if (sequence.LoopSeconds is not { } loopAt || loopAt <= 0 || loopAt >= loopEnd || loopEnd > sequence.EndSeconds)
        {
            throw new FormatException("Prepared music sequence has no valid loop marker.");
        }

        _loop = Segment(sequence, loopAt, loopEnd);
        _backend = backend;
        _voice = new BattleSequenceVoice(Segment(sequence, 0, loopEnd), _backend);
    }

    /// <summary>Gets a value indicating whether the authored loop section has begun.</summary>
    internal bool IsLooping => _looping;

    /// <summary>Gets the number of completed authored track boundaries.</summary>
    internal int LoopCount { get; private set; }

    /// <summary>Advances the authored sequence and restarts at its authored loop point.</summary>
    /// <param name="ticks">Elapsed nominal sixty-hertz ticks.</param>
    public void Advance(int ticks)
    {
        _voice.Advance(ticks);
        if (_voice.HasReachedEnd)
        {
            var bends = _voice.PitchBends;
            _voice.StopAll();
            _looping = true;
            LoopCount++;
            _voice = new BattleSequenceVoice(_loop, _backend, bends);
        }
    }

    /// <summary>Stops the current physical notes and prevents further repetitions.</summary>
    public void Stop() => _voice.StopAll();

    /// <summary>Builds a time-zero sequence holding the authored events in [start, end).</summary>
    /// <param name="source">Complete source sequence.</param>
    /// <param name="start">Segment start time.</param>
    /// <param name="end">Segment end time, where playback jumps.</param>
    /// <returns>The played source segment.</returns>
    private static PreparedAudioSequence Segment(PreparedAudioSequence source, double start, double end) => new(
        [.. source.Commands.Where(command => command.Seconds >= start && command.Seconds < end).Select(command => command with { Seconds = command.Seconds - start })],
        end - start);
}
