using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>Handles <see cref="SoundRequestEvent"/> commands from the action timeline.
/// Each distinct SoundId starts one voice per action; the same pending request does not launch a second voice.
/// All active voices are stopped when the action ends.</summary>
public sealed class BattleAudioConsumer
{
    private readonly IReadOnlyDictionary<int, AudioProgramEntry> _programs;
    private readonly IBattleAudioBackend _backend;
    private readonly Dictionary<int, IVoiceHandle> _activeVoices = [];

    /// <summary>Initializes a new instance of the <see cref="BattleAudioConsumer"/> class.</summary>
    /// <param name="programs">Program-index-to-entry mapping loaded from the audio manifest.</param>
    /// <param name="backend">Audio output backend; use a dry-run backend to prevent hardware playback during probes.</param>
    public BattleAudioConsumer(IReadOnlyDictionary<int, AudioProgramEntry> programs, IBattleAudioBackend backend)
    {
        ArgumentNullException.ThrowIfNull(programs);
        ArgumentNullException.ThrowIfNull(backend);
        _programs = programs;
        _backend = backend;
    }

    /// <summary>Processes one <see cref="BattleSequenceEvent"/>, handling only <see cref="SoundRequestEvent"/>.
    /// All other event types return <see cref="SequenceAcknowledgement.Unavailable"/>.</summary>
    /// <param name="request">The event from the action timeline.</param>
    /// <returns>Applied when the sound was started or is already playing; Unavailable when no program exists for the SoundId.</returns>
    public SequenceAcknowledgement Consume(BattleSequenceEvent request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request is not SoundRequestEvent sound)
        {
            return SequenceAcknowledgement.Unavailable;
        }

        return ConsumeSound(sound);
    }

    /// <summary>Stops all active voices and clears tracking state.
    /// Called by <see cref="BattleRendererSequencePlayback.CancelEffects"/> at the end of each action.</summary>
    public void StopAll()
    {
        foreach (IVoiceHandle voice in _activeVoices.Values)
        {
            _backend.StopVoice(voice);
        }

        _activeVoices.Clear();
    }

    /// <summary>Starts or re-acknowledges a sound request without launching duplicate voices for the same SoundId.</summary>
    /// <param name="sound">The sound request from the script.</param>
    /// <returns>Applied if the voice was started or is already active; Unavailable if no program entry exists.</returns>
    private SequenceAcknowledgement ConsumeSound(SoundRequestEvent sound)
    {
        if (_activeVoices.ContainsKey(sound.SoundId))
        {
            return SequenceAcknowledgement.Applied;
        }

        if (!_programs.TryGetValue(sound.SoundId, out AudioProgramEntry? entry))
        {
            return SequenceAcknowledgement.Unavailable;
        }

        IVoiceHandle handle = _backend.Play(entry);
        _activeVoices[sound.SoundId] = handle;
        return SequenceAcknowledgement.Applied;
    }
}
