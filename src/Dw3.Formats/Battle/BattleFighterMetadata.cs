namespace Dw3.Formats.Battle;

/// <summary>
/// Collected fighter metadata: camera viewpoints, arena geometry and face animation data.
/// These values are decoded from the fighter's separate metadata binary, not from the model file.
/// </summary>
/// <param name="Cameras">List of camera viewpoints for the battle scene; at least one is required.</param>
/// <param name="Geometry">Arena geometry: camera distance and height above the floor.</param>
/// <param name="Face">Decoded face animation sprite data for this fighter.</param>
public sealed record BattleFighterMetadata(IReadOnlyList<BattleCamera> Cameras, BattleGeometry Geometry, BattleFace Face);
