namespace Dw3Production.Conversion;

/// <summary>One frame of a visible NPC's STAND idle loop, already bound to its source
/// decoded PNG, per-frame top-left offset and source-tick duration.</summary>
public sealed record NpcFrame(string Path, int OffsetX, int OffsetY, int Duration);
