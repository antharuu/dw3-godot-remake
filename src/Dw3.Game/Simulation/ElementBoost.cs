namespace Dw3Production.Simulation;

/// <summary>
/// The battlefield's element boost: techniques of <see cref="Element"/> deal more damage and
/// techniques of the element it counters deal less.
/// </summary>
/// <param name="Element">The boosted element; below <see cref="BattleConst.ElementFirst"/> there is no boost.</param>
/// <param name="Amount">The boost strength in 128ths of the damage (half of it is taken off the countered element).</param>
public readonly record struct ElementBoost(int Element, int Amount)
{
    /// <summary>Gets the boost that changes nothing (element 0, amount 0).</summary>
    public static ElementBoost None => default;
}
