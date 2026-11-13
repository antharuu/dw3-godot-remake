namespace Dw3Production.Simulation;

/// <summary>
/// Queries over one side's three fighter slots. A slot counts as living when it holds a
/// fighter with a non-zero Digimon id and positive HP; null slots and id 0 are empty.
/// </summary>
public static class BattleRoster
{
    /// <summary>Returns the first living slot, or -1 when the whole side is down.</summary>
    /// <param name="fighters">The side's slots.</param>
    /// <returns>The slot index, or -1.</returns>
    public static int FirstLivingIndex(IReadOnlyList<BattleFighter?> fighters)
    {
        for (int i = 0; i < fighters.Count; i++)
        {
            if (IsLiving(fighters[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Returns whether at least one slot is living.</summary>
    /// <param name="fighters">The side's slots.</param>
    /// <returns><see langword="true"/> while the side can still fight.</returns>
    public static bool AnyLiving(IEnumerable<BattleFighter?> fighters) => fighters.Any(IsLiving);

    /// <summary>Returns whether the slot holds a fighter that can still fight.</summary>
    /// <param name="fighter">The slot's content.</param>
    /// <returns><see langword="true"/> for a non-empty slot with HP above 0.</returns>
    public static bool IsLiving(BattleFighter? fighter) => fighter is { Id: not 0, Hp: > 0 };
}
