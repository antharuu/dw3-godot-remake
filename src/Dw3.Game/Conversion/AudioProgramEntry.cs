namespace Dw3Production.Conversion;

/// <summary>Resolved attributes for one program in a battle sound bank, ready for playback.</summary>
/// <param name="SampleIndex">Zero-based sample index in the bank (from the 1-based VAG reference minus one).</param>
/// <param name="WavPath">Absolute path to the decoded PCM WAV file for this sample.</param>
/// <param name="Volume">Source tone volume (0–127).</param>
/// <param name="Pan">Source stereo pan (0=left, 64=center, 127=right).</param>
/// <param name="CenterNote">MIDI pitch reference note for the sample.</param>
/// <param name="PitchShift">Fine centre-note adjustment in 1/128-semitone units.</param>
public sealed record AudioProgramEntry(
    int SampleIndex,
    string WavPath,
    byte Volume,
    byte Pan,
    byte CenterNote,
    sbyte PitchShift)
{
    /// <summary>Gets the encoded source sound identifier, distinct from the script index.</summary>
    public uint EncodedId { get; init; }

    /// <summary>Gets the physical VAB program slot.</summary>
    public int Program { get; init; }

    /// <summary>Gets the explicitly selected tone slot.</summary>
    public int Tone { get; init; }

    /// <summary>Gets the requested MIDI note.</summary>
    public int Note { get; init; } = 60;

    /// <summary>Gets the voice arbitration priority, including valid zero.</summary>
    public int Priority { get; init; }

    /// <summary>Gets the product of VAB master and program volume gains.</summary>
    public float BankGain { get; init; } = 1;

    /// <summary>Gets the combined bank and program pan offset about centre.</summary>
    public float PanOffset { get; init; }

    /// <summary>Gets a value indicating whether the decoded sample repeats from its loop marker.</summary>
    public bool Loops { get; init; }

    /// <summary>Gets the sample-frame index of the repeat marker.</summary>
    public int LoopStart { get; init; }

    /// <summary>Gets the positive source tick count until key-off, or zero for natural completion.</summary>
    public int DurationFrames { get; init; }

    /// <summary>Gets the optional decoded attack/decay register for physical envelope playback.</summary>
    public ushort? Adsr1 { get; init; }

    /// <summary>Gets the optional decoded sustain/release register for physical envelope playback.</summary>
    public ushort? Adsr2 { get; init; }
}
