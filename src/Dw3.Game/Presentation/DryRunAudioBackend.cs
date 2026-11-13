using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>Audio backend that reads PCM bytes for verification but routes them to no hardware output.
/// Used in probes and tests to prove sound data is resolved and accessed without audible side effects.</summary>
public sealed class DryRunAudioBackend : IBattleAudioBackend
{
    private readonly List<DryRunVoiceEvent> _events = [];
    private readonly List<AudioReverbChange> _reverbChanges = [];

    /// <summary>Gets all voice-start events recorded since construction, in order.</summary>
    public IReadOnlyList<DryRunVoiceEvent> Events => _events;

    /// <summary>Gets the authored global reverb changes received, in order, without any mixer effect.</summary>
    public IReadOnlyList<AudioReverbChange> ReverbChanges => _reverbChanges;

    /// <inheritdoc/>
    public void ApplyReverb(AudioReverbChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _reverbChanges.Add(change);
    }

    /// <inheritdoc/>
    public IVoiceHandle Play(AudioProgramEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        byte[] wavBytes = File.ReadAllBytes(entry.WavPath);
        var handle = new DryRunHandle(entry.SampleIndex, entry.WavPath, wavBytes.Length);
        _events.Add(new DryRunVoiceEvent(entry.SampleIndex, entry.WavPath, wavBytes.Length, Started: true));
        return handle;
    }

    /// <inheritdoc/>
    public void StopVoice(IVoiceHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (handle is not DryRunHandle dry)
        {
            return;
        }

        _events.Add(new DryRunVoiceEvent(dry.SampleIndex, dry.WavPath, dry.ByteCount, Started: false));
    }

    /// <summary>An immutable event recording one voice start or stop without audio device access.</summary>
    /// <param name="SampleIndex">Zero-based index of the sample in the bank.</param>
    /// <param name="WavPath">Absolute path of the WAV file read during Play.</param>
    /// <param name="ByteCount">Total byte count of the WAV file, verifying PCM was accessed.</param>
    /// <param name="Started">True when the voice was started; false when it was stopped.</param>
    public sealed record DryRunVoiceEvent(int SampleIndex, string WavPath, int ByteCount, bool Started);

    /// <summary>Internal voice handle carrying event metadata for the dry-run backend.</summary>
    private sealed class DryRunHandle(int sampleIndex, string wavPath, int byteCount) : IVoiceHandle
    {
        /// <inheritdoc/>
        public bool IsComplete => false;

        /// <summary>Gets the zero-based sample index.</summary>
        public int SampleIndex { get; } = sampleIndex;

        /// <summary>Gets the absolute WAV path.</summary>
        public string WavPath { get; } = wavPath;

        /// <summary>Gets the total byte count observed when the voice was started.</summary>
        public int ByteCount { get; } = byteCount;
    }
}
