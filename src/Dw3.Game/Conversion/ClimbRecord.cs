namespace Dw3Production.Conversion;

/// <summary>One frame record of an action animation set: frame index, source-tick duration and
/// the sprite draw offset relative to the actor position.</summary>
public sealed record ClimbRecord(int Frame, int Duration, int OffsetX, int OffsetY);
