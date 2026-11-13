namespace Dw3.Formats.Battle;

/// <summary>Decoded attributes of one PS1 VAB tone, sufficient to map a program to its sample reference.</summary>
/// <param name="Priority">Tone arbitration priority. Zero is valid for a declared tone.</param>
/// <param name="Volume">Playback volume (0–127).</param>
/// <param name="Pan">Stereo pan (0=left, 64=center, 127=right).</param>
/// <param name="CenterNote">MIDI note used as the pitch reference centre.</param>
/// <param name="PitchShift">Fine centre-note adjustment in 1/128-semitone units.</param>
/// <param name="MinNote">Lowest MIDI note that activates this tone.</param>
/// <param name="MaxNote">Highest MIDI note that activates this tone.</param>
/// <param name="ProgramRef">Program number stored inside the tone (self-reference, zero-based).</param>
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
    short SampleRef)
{
    /// <summary>Gets the raw attack, decay and sustain-level SPU register.</summary>
    public ushort Adsr1 { get; init; }

    /// <summary>Gets the raw sustain and release SPU register.</summary>
    public ushort Adsr2 { get; init; }
}
