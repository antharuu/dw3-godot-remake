using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>Owns one looping prepared battle track and no unrelated action voices.</summary>
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
        if (sequence.LoopSeconds is not { } loopAt || loopAt <= 0 || loopAt >= sequence.EndSeconds)
        {
            throw new FormatException("Prepared music sequence has no valid loop marker.");
        }

        _loop = BuildLoop(sequence, loopAt);
        _backend = backend;
        _voice = new BattleSequenceVoice(sequence, _backend);
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
            _voice.StopAll();
            _looping = true;
            LoopCount++;
            _voice = new BattleSequenceVoice(_loop, _backend);
        }
    }

    /// <summary>Stops the current physical notes and prevents further repetitions.</summary>
    public void Stop() => _voice.StopAll();

    /// <summary>Builds a time-zero sequence from the authored loop marker onward.</summary>
    /// <param name="source">Complete source sequence.</param>
    /// <param name="loopAt">Authored loop start time.</param>
    /// <returns>The recurring source segment.</returns>
    private static PreparedAudioSequence BuildLoop(PreparedAudioSequence source, double loopAt)
    {
        var carry = source.Commands.Where(command => command.Seconds < loopAt && command.PitchBend is not null)
            .GroupBy(command => command.Channel)
            .Select(group => group.Last() with { Seconds = 0 });
        var segment = source.Commands.Where(command => command.Seconds >= loopAt)
            .Select(command => command with { Seconds = command.Seconds - loopAt });
        return new PreparedAudioSequence([.. carry.Concat(segment).OrderBy(command => command.Seconds)], source.EndSeconds - loopAt, 0);
    }
}
