namespace Dw3Production.Simulation;

/// <summary>Provides source-qualified selection and opening rules for <see cref="BattleTurnScheduler"/>.</summary>
internal static class BattleTurnSchedulerRules
{
    /// <summary>Tests the optional encounter ambush chance.</summary>
    /// <param name="ambushChance">Encounter chance, zero when disabled.</param>
    /// <param name="rng">Injected shared source.</param>
    /// <returns>Whether the enemy receives the opening turn.</returns>
    internal static bool IsAmbush(int ambushChance, IBattleRng rng) =>
        ambushChance > 0 && (rng.NextValue() % 128) < ambushChance;

    /// <summary>Returns the event kind associated with a turn side.</summary>
    /// <param name="side">Battle side.</param>
    /// <returns>Its ordinary turn kind.</returns>
    internal static BattleScheduledEventKind TurnKind(BattleSide side) =>
        side == BattleSide.Player ? BattleScheduledEventKind.PlayerTurn : BattleScheduledEventKind.EnemyTurn;

    /// <summary>Tests whether a scanned event replaces the current selected event.</summary>
    /// <param name="best">Current selected event.</param>
    /// <param name="candidate">Later scanned event.</param>
    /// <returns>Whether the candidate wins selection.</returns>
    internal static bool ShouldReplaceBest(BattleScheduledEvent best, BattleScheduledEvent candidate) =>
        candidate.Delay < best.Delay || (candidate.Delay == best.Delay && IsTurn(best));

    /// <summary>Tests whether an event is a player or enemy turn.</summary>
    /// <param name="item">Event to inspect.</param>
    /// <returns>Whether it is an ordinary turn.</returns>
    private static bool IsTurn(BattleScheduledEvent item) => item.Kind is BattleScheduledEventKind.PlayerTurn or BattleScheduledEventKind.EnemyTurn;
}
