namespace Dw3.Formats.Battle;

/// <summary>
/// Camera placement offsets for a battle scene, read from a fighter model's metadata block.
/// These values set the initial camera angle relative to the arena floor; larger distance
/// moves the camera further back, larger height moves it up.
/// </summary>
/// <param name="Distance">Horizontal distance from the arena centre to the camera eye, in world units.</param>
/// <param name="Height">Vertical offset from the arena floor to the camera eye, in world units.</param>
public sealed record BattleGeometry(int Distance, int Height);
