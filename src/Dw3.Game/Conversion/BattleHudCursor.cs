namespace Dw3Production.Conversion;

/// <summary>The battle-HUD cursor bar: one PNG holding 12 frames of 12x12 side by side.</summary>
public sealed record BattleHudCursor(string Path, string OffPath, int Width, int Height, int Frames, int FrameW, int FrameH);
