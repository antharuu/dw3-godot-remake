namespace Dw3.Formats.Battle;

/// <summary>One prepared MIDI-style event with its original relative tick delta.</summary>
/// <param name="DeltaTicks">Nonnegative ticks since the preceding event.</param>
/// <param name="Status">Channel status or 0xFF for metadata.</param>
/// <param name="Data1">First channel byte or metadata type.</param>
/// <param name="Data2">Second channel byte or metadata integer.</param>
public sealed record SequenceMidiEvent(int DeltaTicks, int Status, int Data1, int Data2);
