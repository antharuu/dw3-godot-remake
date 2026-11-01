namespace Dw3.Extractor;

/// <summary>One timed animation frame with its tile-relative draw offset.</summary>
public sealed record NpcAnimRecord(int Duration, int Frame, int OffsetX, int OffsetY);
