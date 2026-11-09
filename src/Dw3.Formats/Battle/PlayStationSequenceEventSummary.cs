namespace Dw3.Formats.Battle;

/// <summary>Aggregate event metadata that does not retain SEP event payloads.</summary>
/// <param name="EventCount">Number of events, including the terminating end-of-track event.</param>
/// <param name="ChannelEventCount">Number of channel voice events.</param>
/// <param name="MetaEventCount">Number of meta events, including end-of-track.</param>
/// <param name="SystemEventCount">Number of system exclusive or system common events.</param>
/// <param name="TotalDeltaTicks">Checked sum of event delta times.</param>
/// <param name="HasEndOfTrack">Whether parsing observed one valid zero-length end-of-track event.</param>
public sealed record PlayStationSequenceEventSummary(
    int EventCount,
    int ChannelEventCount,
    int MetaEventCount,
    int SystemEventCount,
    int TotalDeltaTicks,
    bool HasEndOfTrack);
