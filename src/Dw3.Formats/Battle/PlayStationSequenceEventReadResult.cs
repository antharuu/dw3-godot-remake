namespace Dw3.Formats.Battle;

/// <summary>Internal event-stream outcome used while decoding one SEP track.</summary>
/// <param name="NextOffset">Exclusive offset after the valid end-of-track event.</param>
/// <param name="Summary">Aggregate event metadata safe to retain outside the source bytes.</param>
internal sealed record PlayStationSequenceEventReadResult(int NextOffset, PlayStationSequenceEventSummary Summary);
