namespace Dw3.Formats.Battle;

/// <summary>One tone in a sparse program table with its physical selection indices.</summary>
/// <param name="ProgramIndex">Physical program slot, not the compact tone-row index.</param>
/// <param name="ToneIndex">Tone slot within the program.</param>
/// <param name="ProgramVolume">Program gain, 0 through 127.</param>
/// <param name="ProgramPan">Program pan, 0 through 127.</param>
/// <param name="Attributes">Decoded tone parameters, including a valid zero priority.</param>
public sealed record VabProgramTone(int ProgramIndex, int ToneIndex, byte ProgramVolume, byte ProgramPan, VabToneAttributes Attributes);
