namespace Dw3.Formats.Battle;

/// <summary>Identifies the safe category counted for one decoded SEP event.</summary>
internal enum PlayStationSequenceEventKind
{
    /// <summary>A channel voice event.</summary>
    Channel,

    /// <summary>A meta event.</summary>
    Meta,

    /// <summary>A system exclusive or system common event.</summary>
    System,
}
