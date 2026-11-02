namespace Dw3Production.Conversion;

/// <summary>One battle-HUD sprite frame (HP-bar cap, background or technique gauge): its PNG
/// path and pixel size. Loaded by LoadBattleHud; every frame is a disc-decoded sheet sprite.</summary>
public sealed record BattleHudFrame(int FrameId, IReadOnlyList<string> ClutRows, int Width, int Height, int OffsetX, int OffsetY);
