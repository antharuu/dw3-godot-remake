namespace Dw3Production.Conversion;

/// <summary>One CMFONTCS battle-menu frame, including source anchor offset and CLUT variants.</summary>
public sealed record BattleMenuIconFrame(int FrameId, IReadOnlyList<string> ClutRows, int Width, int Height, int OffsetX, int OffsetY);
