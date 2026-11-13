using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Session resources a victory may change exactly once, compared before and after each report checkpoint.</summary>
/// <param name="Position">Field position of the player.</param>
/// <param name="LeadExp">Cumulative experience of the lead partner.</param>
/// <param name="LeadLevel">Level of the lead partner.</param>
/// <param name="Money">Session wallet.</param>
/// <param name="Item">Item id the report awards, 0 for none.</param>
/// <param name="ItemCount">Session stock of <paramref name="Item"/>.</param>
internal readonly record struct BattleReportSessionSnapshot(NumericsVector2 Position, int LeadExp, int LeadLevel, int Money, int Item, int ItemCount)
{
    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"pos=({Position.X:0},{Position.Y:0}) leadExp={LeadExp} leadLevel={LeadLevel} money={Money} item={Item} stock={ItemCount}");
}
