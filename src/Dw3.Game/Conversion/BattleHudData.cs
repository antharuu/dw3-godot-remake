namespace Dw3Production.Conversion;

/// <summary>The battle HUD contract (schema <c>m6-battle-hud-v1</c>): the sheet frames the HUD draws plus the cursor.</summary>
public sealed record BattleHudData(IReadOnlyDictionary<int, BattleHudFrame> Frames, BattleHudCursor Cursor);
