using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>Arms the one-shot entrance cue after the ready incoming model's camera lead-in.</summary>
internal sealed class BattleEntranceAudio
{
    private readonly Action<AudioProgramEntry> _play;
    private AudioProgramEntry? _entry;
    private int _remaining;

    /// <summary>Initializes a new instance of the <see cref="BattleEntranceAudio"/> class.</summary>
    /// <param name="play">Real backend operation, shared with action voices.</param>
    public BattleEntranceAudio(Action<AudioProgramEntry> play) => _play = play;

    /// <summary>Gets how many cues this runtime played.</summary>
    public int Plays { get; private set; }

    /// <summary>Arms one new entrance after model readiness, replacing an unstarted entrance.</summary>
    /// <param name="delay">Authored nominal 60 Hz lead-in.</param>
    /// <param name="entry">Decoded direct cue, not an action index.</param>
    public void Begin(int delay, AudioProgramEntry entry)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(delay);
        _remaining = delay;
        _entry = entry;
    }

    /// <summary>Disarms a pending entrance during battle teardown or interruption.</summary>
    public void Cancel() => _entry = null;

    /// <summary>Fires once when the source-clock lead-in has elapsed.</summary>
    /// <param name="ticks">Elapsed nominal source units.</param>
    public void Advance(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        if (_entry is null)
        {
            return;
        }

        _remaining -= ticks;
        if (_remaining <= 0)
        {
            _play(_entry);
            _entry = null;
            Plays++;
        }
    }
}
