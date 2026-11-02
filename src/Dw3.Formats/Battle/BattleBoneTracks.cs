namespace Dw3.Formats.Battle;

/// <summary>
/// The three keyframe tracks (translation, rotation, scale) for one bone in a battle model's
/// key archive. Each track is a sorted list of <see cref="BattleTrackKey"/> entries decoded from
/// the archive's 8-byte-per-key layout.
/// </summary>
/// <param name="Translation">World-space translation keys for this bone, ordered by ascending frame index.</param>
/// <param name="Rotation">Rotation keys for this bone, in the same fixed-point unit as the model's scale.</param>
/// <param name="Scale">Scale keys for this bone, in the same fixed-point unit as rotation.</param>
public sealed record BattleBoneTracks(
    IReadOnlyList<BattleTrackKey> Translation,
    IReadOnlyList<BattleTrackKey> Rotation,
    IReadOnlyList<BattleTrackKey> Scale)
{
    /// <summary>Gets all three tracks as an ordered list: [Translation, Rotation, Scale].</summary>
    public IReadOnlyList<IReadOnlyList<BattleTrackKey>> All => [Translation, Rotation, Scale];
}
