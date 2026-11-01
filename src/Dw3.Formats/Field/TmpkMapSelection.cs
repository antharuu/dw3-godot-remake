namespace Dw3.Formats.Field;

/// <summary>Disc-proven TMPK archive entries selected by a stage overlay.</summary>
public sealed record TmpkMapSelection(int FloorEntry, int? TriggerEntry, int? AreasEntry);
