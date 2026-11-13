namespace Dw3Production.Presentation;

/// <summary>An opaque handle to one active audio voice started by <see cref="IBattleAudioBackend.Play"/>.</summary>
public interface IVoiceHandle
{
    /// <summary>Gets a value indicating whether the voice has finished playing naturally.</summary>
    bool IsComplete { get; }

    /// <summary>Gets the current normalized ADSR amplitude for equal-priority arbitration.</summary>
    float EnvelopeLevel => 1;

    /// <summary>Advances physical envelope state on the nominal source clock.</summary>
    /// <param name="ticks">Elapsed 60 Hz units.</param>
    void Advance(int ticks)
    {
    }

    /// <summary>Begins an envelope release when supported by the physical handle.</summary>
    /// <returns>True when release is handled without an immediate hard stop.</returns>
    bool KeyOff() => false;

    /// <summary>Scales this voice's volume, as a sequence decrescendo does.</summary>
    /// <param name="scale">Volume factor from 0 (silent) to 1 (authored volume).</param>
    void SetVolumeScale(float scale)
    {
    }

    /// <summary>Applies the decoded source bend to this physical voice.</summary>
    /// <param name="semitones">Signed pitch displacement in semitones.</param>
    void SetPitchBend(float semitones)
    {
    }
}
