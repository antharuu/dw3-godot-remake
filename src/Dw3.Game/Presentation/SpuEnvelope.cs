namespace Dw3Production.Presentation;

/// <summary>Sample-clock ADSR state used by physical voices and envelope-sensitive arbitration.</summary>
internal sealed class SpuEnvelope
{
    private readonly ushort _first;
    private readonly ushort _second;
    private int _phase;
    private int _counter;

    /// <summary>Initializes a new instance of the <see cref="SpuEnvelope"/> class.</summary>
    /// <param name="first">Attack, decay and sustain-level register.</param>
    /// <param name="second">Sustain and release register.</param>
    public SpuEnvelope(ushort first, ushort second)
    {
        _first = first;
        _second = second;
    }

    /// <summary>Gets the current 15-bit envelope level.</summary>
    public int Level { get; private set; }

    /// <summary>Gets a value indicating whether a decreasing envelope reached silence.</summary>
    public bool IsComplete => _phase >= 2 && Level == 0;

    /// <summary>Starts release without retriggering attack or resetting amplitude.</summary>
    public void KeyOff()
    {
        _phase = 3;
        _counter = 0;
    }

    /// <summary>Advances the envelope using unpitched 44.1 kHz output samples.</summary>
    /// <param name="samples">Elapsed mixer-rate samples.</param>
    public void AdvanceSamples(int samples)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(samples);
        for (int index = 0; index < samples && !IsComplete; index++)
        {
            AdvanceSample();
        }
    }

    /// <summary>Applies the register fields for the current phase and handles its threshold.</summary>
    private void AdvanceSample()
    {
        if (_phase == 3 && (_second & 31) == 31)
        {
            return;
        }

        Level = _phase switch
        {
            0 => SpuEnvelopeRate.Advance(ref _counter, Level, (_first >> 10) & 31, (_first >> 8) & 3, false, (_first & 0x8000) != 0),
            1 => SpuEnvelopeRate.Advance(ref _counter, Level, (_first >> 4) & 15, 0, true, true),
            2 => SpuEnvelopeRate.Advance(ref _counter, Level, (_second >> 8) & 31, (_second >> 6) & 3, (_second & 0x4000) != 0, (_second & 0x8000) != 0),
            _ => SpuEnvelopeRate.Advance(ref _counter, Level, _second & 31, 0, true, (_second & 32) != 0),
        };
        if ((_phase == 0 && Level == 32767) || (_phase == 1 && Level <= ((_first & 15) + 1) * 2048))
        {
            _phase++;
            _counter = 0;
        }
    }
}
