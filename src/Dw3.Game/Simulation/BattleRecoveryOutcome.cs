namespace Dw3Production.Simulation;

/// <summary>
/// Frozen rule result of a healing technique, read by the result message once its script ends: the fighters the
/// message names and the HP amount it prints, or the user alone when the heal had no effect.
/// </summary>
/// <param name="Side">Side of the user and of every healed fighter.</param>
/// <param name="SideWide">Whether the technique heals the user's whole living side instead of its active fighter.</param>
/// <param name="NamedIds">Fighter ids named by the message: the hurt living fighters before the heal, or the user when nothing was healed.</param>
/// <param name="Amount">HP amount printed by the message, zero when the heal had no effect.</param>
public sealed record BattleRecoveryOutcome(BattleSide Side, bool SideWide, IReadOnlyList<int> NamedIds, int Amount)
{
    /// <summary>Gets a value indicating whether the heal restored HP to at least one fighter.</summary>
    public bool Healed => Amount > 0;
}
