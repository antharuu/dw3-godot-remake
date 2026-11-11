namespace Dw3.Formats.Battle;

/// <summary>Decoded attributes of one PS1 VAB tone, sufficient to map a program to its sample reference.</summary>
/// <param name="Priority">Tone priority; zero marks an unused slot.</param>
/// <param name="Volume">Playback volume (0–127).</param>
/// <param name="Pan">Stereo pan (0=left, 64=center, 127=right).</param>
/// <param name="CenterNote">MIDI note used as the pitch reference centre.</param>
/// <param name="PitchShift">Fine pitch shift in semitones (signed).</param>
/// <param name="MinNote">Lowest MIDI note that activates this tone.</param>
/// <param name="MaxNote">Highest MIDI note that activates this tone.</param>
/// <param name="ProgramRef">1-based program number stored inside the tone (self-reference).</param>
/// <param name="SampleRef">1-based VAG sample number; zero means silent.</param>
public sealed record VabToneAttributes(
    byte Priority,
    byte Volume,
    byte Pan,
    byte CenterNote,
    sbyte PitchShift,
    byte MinNote,
    byte MaxNote,
    short ProgramRef,
    short SampleRef);
