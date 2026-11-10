namespace Dw3.Formats.Battle;

/// <summary>
/// A fighter resolved to its model, motion and action-effect archives. References are disc file ids
/// (indices into the executable's file-sector table) paired with an entry of that file's archive.
/// </summary>
public sealed record FighterModelReference(
    int FighterId, int Index, int Kind, bool IsEnemy, int ModelFile, int ModelEntry, int MotionFile, int MotionEntry, int EffectFile = -1, int EffectEntry = -1);
