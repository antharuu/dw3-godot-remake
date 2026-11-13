using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>Contract for the backend that physically starts and stops battle sound voices.</summary>
public interface IBattleAudioBackend
{
    /// <summary>Validates and prepares a note before its sequence is acknowledged or scheduled.</summary>
    /// <param name="entry">Resolved sample and playback attributes.</param>
    void Prepare(AudioProgramEntry entry)
    {
    }

    /// <summary>Starts playing the given sample and returns a handle for later stopping it.</summary>
    /// <param name="entry">Resolved program entry with the WAV path and playback attributes.</param>
    /// <returns>A handle uniquely identifying this playing voice.</returns>
    IVoiceHandle Play(AudioProgramEntry entry);

    /// <summary>Stops the voice identified by the given handle. Safe to call after natural completion.</summary>
    /// <param name="handle">Handle returned by a previous <see cref="Play"/> call.</param>
    void StopVoice(IVoiceHandle handle);

    /// <summary>Applies an authored change of the global SPU reverb state to the physical output.</summary>
    /// <param name="change">Reverb type or depth in source units.</param>
    /// <exception cref="NotSupportedException">The backend has no physical reverb consumer.</exception>
    void ApplyReverb(AudioReverbChange change) =>
        throw new NotSupportedException($"This audio backend cannot apply the required reverb {change}.");
}
