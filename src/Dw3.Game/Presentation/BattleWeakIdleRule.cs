namespace Dw3Production.Presentation;

/// <summary>
/// The source rule that picks a fighter's idle after an action or at its entrance: the weak idle when the damage about
/// to be shown leaves the fighter at a quarter of its maximum HP or less. Healing is passed as negative damage.
/// </summary>
internal static class BattleWeakIdleRule
{
    /// <summary>Normal idle selection.</summary>
    public const int Normal = 0;

    /// <summary>Weak idle selection.</summary>
    public const int Weak = 1;

    /// <summary>Selects the idle with the source integer quarter.</summary>
    /// <param name="hp">Current HP before the shown damage.</param>
    /// <param name="damage">Damage about to be shown, negative for healing, zero at an entrance.</param>
    /// <param name="maxHp">Maximum HP.</param>
    /// <returns><see cref="Weak"/> when <c>hp - damage &lt;= maxHp / 4</c>, otherwise <see cref="Normal"/>.</returns>
    public static int Select(int hp, int damage, int maxHp) => hp - damage <= maxHp / 4 ? Weak : Normal;
}
