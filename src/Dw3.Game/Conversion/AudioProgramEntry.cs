namespace Dw3Production.Conversion;

/// <summary>Resolved attributes for one program in a battle sound bank, ready for playback.</summary>
/// <param name="SampleIndex">Zero-based sample index in the bank (from the 1-based VAG reference minus one).</param>
/// <param name="WavPath">Absolute path to the decoded PCM WAV file for this sample.</param>
/// <param name="Volume">Source tone volume (0–127).</param>
/// <param name="Pan">Source stereo pan (0=left, 64=center, 127=right).</param>
/// <param name="CenterNote">MIDI pitch reference note for the sample.</param>
/// <param name="PitchShift">Fine pitch shift in semitones (signed).</param>
public sealed record AudioProgramEntry(
    int SampleIndex,
    string WavPath,
    byte Volume,
    byte Pan,
    byte CenterNote,
    sbyte PitchShift);
