namespace Dw3.Formats.Battle;

/// <summary>Safe structural metadata for one SEP track.</summary>
/// <param name="Number">Zero-based track number declared by the track header.</param>
/// <param name="HeaderOffset">Absolute byte offset of the fixed track header.</param>
/// <param name="EventsOffset">Absolute byte offset of the first variable-length event.</param>
/// <param name="EndOffset">Exclusive absolute byte offset immediately after the end-of-track event.</param>
/// <param name="TicksPerQuarterNote">Timing resolution declared by the track header.</param>
/// <param name="TempoMicrosecondsPerQuarterNote">Three-byte tempo declared by the track header.</param>
/// <param name="Events">Safe event counts and completion state.</param>
public sealed record PlayStationSequenceTrack(
    ushort Number,
    int HeaderOffset,
    int EventsOffset,
    int EndOffset,
    ushort TicksPerQuarterNote,
    uint TempoMicrosecondsPerQuarterNote,
    PlayStationSequenceEventSummary Events);
