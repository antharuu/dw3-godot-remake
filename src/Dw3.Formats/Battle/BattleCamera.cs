namespace Dw3.Formats.Battle;

/// <summary>
/// Camera parameters for one battle scene viewpoint, decoded from a fighter's metadata block.
/// Position and reference are in PS1 world units (integers). Projection is the GTE screen distance
/// (h parameter of RotTransPers), typically 400 for battle scenes.
/// </summary>
/// <param name="PosX">Camera eye X in world units.</param>
/// <param name="PosY">Camera eye Y in world units (positive = up).</param>
/// <param name="PosZ">Camera eye Z in world units.</param>
/// <param name="RefX">Look-at reference point X in world units.</param>
/// <param name="RefY">Look-at reference point Y in world units.</param>
/// <param name="RefZ">Look-at reference point Z in world units.</param>
/// <param name="Projection">GTE screen distance (h); controls the perspective strength.</param>
public sealed record BattleCamera(int PosX, int PosY, int PosZ, int RefX, int RefY, int RefZ, int Projection);
