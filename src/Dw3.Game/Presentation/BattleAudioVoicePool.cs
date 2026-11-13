using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>Bounded physical voice lifetime and priority arbitration in source timing units.</summary>
internal sealed class BattleAudioVoicePool : IBattleAudioBackend
{
    private readonly Func<AudioProgramEntry, IVoiceHandle> _start;
    private readonly Action<IVoiceHandle> _stop;
    private readonly List<Voice> _voices = [];
    private readonly int _capacity;

    /// <summary>Initializes a new instance of the <see cref="BattleAudioVoicePool"/> class.</summary>
    /// <param name="start">Physical mixer start operation.</param>
    /// <param name="stop">Physical mixer key-off operation.</param>
    /// <param name="capacity">Available SPU-style channels, at most 24.</param>
    public BattleAudioVoicePool(Func<AudioProgramEntry, IVoiceHandle> start, Action<IVoiceHandle> stop, int capacity = 24)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 24);
        _start = start;
        _stop = stop;
        _capacity = capacity;
    }

    /// <summary>Gets the number of allocated physical channels.</summary>
    public int ActiveCount => _voices.Count;

    /// <inheritdoc/>
    public IVoiceHandle Play(AudioProgramEntry entry)
    {
        Advance(0);
        if (_voices.Count == _capacity)
        {
            var victim = _voices.OrderBy(voice => voice.Entry.Priority).ThenBy(voice => voice.Handle.EnvelopeLevel).First();
            if (victim.Entry.Priority > entry.Priority)
            {
                throw new IOException($"battle-audio-channels-busy priority={entry.Priority} channels={_capacity}");
            }

            StopVoice(victim.Handle);
        }

        var handle = _start(entry);
        _voices.Add(new Voice(entry, handle, entry.DurationFrames));
        return handle;
    }

    /// <inheritdoc/>
    public void StopVoice(IVoiceHandle handle)
    {
        _stop(handle);
        _voices.RemoveAll(voice => ReferenceEquals(voice.Handle, handle));
    }

    /// <summary>Keys off expired timed notes and retires naturally completed one-shots.</summary>
    /// <param name="ticks">Elapsed 60 Hz source units.</param>
    public void Advance(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        foreach (var voice in _voices.ToArray())
        {
            voice.Handle.Advance(ticks);
            voice.Remaining -= ticks;
            if (voice.Handle.IsComplete)
            {
                StopVoice(voice.Handle);
            }
            else if (voice.Entry.DurationFrames > 0 && voice.Remaining <= 0 && !voice.Released)
            {
                voice.Released = true;
                if (!voice.Handle.KeyOff())
                {
                    StopVoice(voice.Handle);
                }
            }
        }
    }

    /// <summary>Releases all channels, including notes trailing beyond an action's End.</summary>
    public void StopAll()
    {
        foreach (var voice in _voices.ToArray())
        {
            StopVoice(voice.Handle);
        }
    }

    /// <summary>Allocation state for a physically started note.</summary>
    /// <param name="Entry">Resolved attributes.</param>
    /// <param name="Handle">Actual voice handle.</param>
    /// <param name="Remaining">Remaining timed key-on units.</param>
    private sealed record Voice(AudioProgramEntry Entry, IVoiceHandle Handle, int Remaining)
    {
        /// <summary>Gets or sets remaining units independently of immutable source attributes.</summary>
        public int Remaining { get; set; } = Remaining;

        /// <summary>Gets or sets a value indicating whether timed key-off has already been sent.</summary>
        public bool Released { get; set; }
    }
}
