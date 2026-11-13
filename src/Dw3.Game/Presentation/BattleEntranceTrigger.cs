using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>
/// Decides whether a change of the active pair is a source entrance and why. The source starts an entrance whenever
/// another slot comes in, whatever its fighter id (two enemies of the same kind still switch through an entrance),
/// while a form change of the same slot is a digimon change, not an entrance.
/// </summary>
internal static class BattleEntranceTrigger
{
    /// <summary>Resolves the entrance a changed active pair calls for.</summary>
    /// <param name="state">Battle state, already mutated.</param>
    /// <param name="rendered">Pair the scene shows.</param>
    /// <param name="changedByAction">Whether the change was made at the end of an action sequence (a pair technique's tag-in).</param>
    /// <returns>The entrance kind, or <see cref="BattleEntranceKind.None"/>.</returns>
    public static BattleEntranceKind Resolve(BattleState state, BattleRenderedPair rendered, bool changedByAction)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (rendered.PlayerSlot < 0 || rendered.EnemySlot < 0)
        {
            return BattleEntranceKind.None;
        }

        int enemyId = state.ActiveEnemyFighter?.Id ?? 0;
        bool playerChanged = state.ActivePlayer != rendered.PlayerSlot;
        bool enemyChanged = state.ActiveEnemy != rendered.EnemySlot || enemyId != rendered.EnemyId;
        if (playerChanged == enemyChanged)
        {
            return BattleEntranceKind.None;
        }

        if (playerChanged)
        {
            return changedByAction ? BattleEntranceKind.PlayerTagIn : BattleEntranceKind.PlayerSwitch;
        }

        if ((state.ActivePlayerFighter?.Id ?? 0) != rendered.PlayerId)
        {
            return BattleEntranceKind.None;
        }

        return OutgoingEnemyKnockedOut(state, rendered) ? BattleEntranceKind.EnemyReplacement : BattleEntranceKind.EnemySwitch;
    }

    /// <summary>Returns the idle the source gives the incoming fighter: a voluntary enemy switch always enters normal,
    /// every other caller asks <see cref="BattleWeakIdleRule"/> with the incoming fighter's HP.</summary>
    /// <param name="kind">Entrance kind.</param>
    /// <param name="entering">Incoming fighter.</param>
    /// <returns>Zero for the normal idle, one for the weak idle.</returns>
    public static int Idle(BattleEntranceKind kind, BattleFighter entering)
    {
        ArgumentNullException.ThrowIfNull(entering);
        return kind == BattleEntranceKind.EnemySwitch ? BattleWeakIdleRule.Normal : BattleWeakIdleRule.Select(entering.Hp, 0, entering.MaxHp);
    }

    /// <summary>Returns whether the rendered enemy slot was left on a knockout.</summary>
    /// <param name="state">Battle state.</param>
    /// <param name="rendered">Pair the scene shows.</param>
    /// <returns>Whether the outgoing enemy is knocked out.</returns>
    private static bool OutgoingEnemyKnockedOut(BattleState state, BattleRenderedPair rendered) =>
        rendered.EnemySlot < state.Enemies.Length && state.Enemies[rendered.EnemySlot] is { IsKnockedOut: true };
}
