namespace Dw3Production.Conversion;

/// <summary>One resolved note transition at an authored sequence time.</summary>
/// <param name="Seconds">Absolute time from sequence start.</param>
/// <param name="Channel">Original MIDI channel.</param>
/// <param name="Note">Original MIDI note.</param>
/// <param name="Entry">Resolved note-on, or null for note-off.</param>
public sealed record AudioSequenceCommand(double Seconds, int Channel, int Note, AudioProgramEntry? Entry);
