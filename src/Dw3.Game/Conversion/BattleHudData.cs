namespace Dw3Production.Conversion;

/// <summary>The m6-battle-hud-v1 contract: the sheet frames FIGHTSTG_drawHud draws plus the cursor.</summary>
public sealed record BattleHudData(IReadOnlyDictionary<int, BattleHudFrame> Frames, BattleHudCursor Cursor);
