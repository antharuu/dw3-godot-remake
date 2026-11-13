namespace Dw3Production.Presentation;

/// <summary>The party slots and fighter ids the battle scene currently shows, -1 for a slot not shown yet.</summary>
/// <param name="PlayerSlot">Rendered player party slot.</param>
/// <param name="PlayerId">Rendered player fighter id.</param>
/// <param name="EnemySlot">Rendered enemy slot.</param>
/// <param name="EnemyId">Rendered enemy fighter id.</param>
internal readonly record struct BattleRenderedPair(int PlayerSlot, int PlayerId, int EnemySlot, int EnemyId)
{
    /// <summary>Gets the pair of a battle whose models are not built yet.</summary>
    public static BattleRenderedPair Empty { get; } = new(-1, 0, -1, 0);
}
