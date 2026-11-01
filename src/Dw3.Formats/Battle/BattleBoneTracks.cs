namespace Dw3.Formats.Battle;

public sealed record BattleBoneTracks(
    IReadOnlyList<BattleTrackKey> Translation,
    IReadOnlyList<BattleTrackKey> Rotation,
    IReadOnlyList<BattleTrackKey> Scale)
{
    public IReadOnlyList<IReadOnlyList<BattleTrackKey>> All => [Translation, Rotation, Scale];
}
