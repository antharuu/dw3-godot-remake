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
}
