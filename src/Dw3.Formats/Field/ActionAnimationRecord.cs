namespace Dw3.Formats.Field;

/// <summary>One timed action animation frame and its tile-relative draw offset.</summary>
public sealed record ActionAnimationRecord(int Duration, int Frame, int OffsetX, int OffsetY);
