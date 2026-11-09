namespace Dw3.Formats.Battle;

/// <summary>Safe structural metadata for a PlayStation SEP sequence entry.</summary>
/// <param name="Offset">Absolute byte offset of the <c>pQES</c> signature in the MP archive.</param>
/// <param name="Length">Validated SEP entry length in bytes.</param>
/// <param name="Tracks">Validated track metadata without source event payloads.</param>
public sealed record PlayStationSequence(int Offset, int Length, IReadOnlyList<PlayStationSequenceTrack> Tracks);
