namespace Dw3.Formats.Battle;

/// <summary>Complete portable fighter model, including render assets and animation data.</summary>
public sealed record BattleModelPayload(
    BattleModelStructure Structure,
    IReadOnlyDictionary<int, BattleBoneTracks> Tracks,
    BattleTextureSet Textures,
    IReadOnlyList<IReadOnlyList<BattleMotionStep>> Motions,
    BattleFighterMetadata Metadata);
