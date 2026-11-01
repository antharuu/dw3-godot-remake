namespace Dw3.Formats.Battle;

public sealed record BattleFighterMetadata(IReadOnlyList<BattleCamera> Cameras, BattleGeometry Geometry, BattleFace Face);
