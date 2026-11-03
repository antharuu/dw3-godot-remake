namespace Dw3Production.Simulation;

/// <summary>Evaluates enemy action tables and resolves their battle decisions.</summary>
public static class BattleEnemyAi
{
    /// <summary>The conventional fallback row in a four-row action table.</summary>
    public const int BattleTableFallback = 3;

    /// <summary>Evaluates one action-table condition against the current battle state.</summary>
    /// <param name="condition">Decoded action-table condition id.</param>
    /// <param name="arg">Decoded condition argument.</param>
    /// <param name="ctx">Battle state exposed to condition evaluation.</param>
    /// <param name="rng">Random source for chance conditions.</param>
    /// <returns>
    /// Whether the condition holds. Condition 0 and unknown ids always hold, so a malformed row
    /// still acts. HP thresholds take <paramref name="arg"/> as a share of maximum HP in 128ths,
    /// condition 1 is a chance of <paramref name="arg"/> in 128, and condition 18 holds every
    /// <paramref name="arg"/> enemy turns. Only condition 1 draws from <paramref name="rng"/>.
    /// </returns>
    public static bool TestEnemyCondition(int condition, int arg, EnemyAiContext ctx, IBattleRng rng)
    {
        BattleFighter enemy = ctx.Enemy;
        BattleFighter? player = ctx.ActivePlayer;

        return condition switch
        {
            0 => true,
            1 => rng.NextValue() % 128 < arg,
            2 => enemy.Hp < HpShare(enemy.MaxHp, arg),
            3 => enemy.Hp >= HpShare(enemy.MaxHp, arg),
            4 => enemy.Mp < arg,
            5 => enemy.Mp >= arg,
            6 => player is not null && player.Hp < HpShare(player.MaxHp, arg),
            7 => player is not null && player.Hp >= HpShare(player.MaxHp, arg),
            8 => player is not null && ctx.StarterDigimonIds.Contains(player.Id),
            9 => player is not null && (player.Flags & FighterFlags.Asleep) != 0,
            10 => HasMatchingReserve(ctx, arg),
            11 => ctx.BoostElement == arg,
            12 => ctx.BattleId == arg,
            13 => ctx.EncounterKind == arg,
            14 => enemy.Charge != 0,
            15 => enemy.Boosts[0] < 0,
            16 => enemy.Boosts[1] < 0,
            17 => enemy.Boosts[2] < 0,
            18 => arg != 0 && enemy.Turns % arg == 0,
            _ => true,
        };
    }

    /// <summary>Converts an action-table target kind into an executable decision.</summary>
    /// <param name="enemy">Enemy whose action is selected.</param>
    /// <param name="kind">Decoded action-table target kind: 1 attack, 2 and 3 the second and third techniques, 4 run away, 5..7 switch to a given slot, 8 switch to any living reserve.</param>
    /// <returns>The decision with the technique id to use or the slot to switch to, or <see cref="EnemyDecision.None"/> for any other kind.</returns>
    public static EnemyDecision GetEnemyAction(BattleFighter enemy, int kind) => kind switch
    {
        1 => new(EnemyActionKind.Attack, enemy.Techs[0], -1),
        2 => new(EnemyActionKind.Technique1, enemy.Techs[1], -1),
        3 => new(EnemyActionKind.Technique2, enemy.Techs[2], -1),
        4 => new(EnemyActionKind.RunAway, 0, -1),
        5 => new(EnemyActionKind.SwitchTo0, 0, 0),
        6 => new(EnemyActionKind.SwitchTo1, 0, 1),
        7 => new(EnemyActionKind.SwitchTo2, 0, 2),
        8 => new(EnemyActionKind.SwitchToAny, 0, -1),
        _ => EnemyDecision.None,
    };

    /// <summary>Selects the first eligible action row, or the table's untested fallback row.</summary>
    /// <remarks>Rows before the fallback are tested in table order and the first one whose condition holds wins, so earlier rows have priority. A table shorter than four rows uses its last row as the fallback.</remarks>
    /// <param name="enemy">Enemy whose action table is evaluated.</param>
    /// <param name="ctx">Battle state exposed to condition evaluation.</param>
    /// <param name="rng">Random source for chance conditions.</param>
    /// <returns>The chosen decision, or <see cref="EnemyDecision.None"/> when the enemy has no action row.</returns>
    public static EnemyDecision PickAction(BattleFighter enemy, EnemyAiContext ctx, IBattleRng rng)
    {
        if (enemy.Actions.Length == 0)
        {
            return EnemyDecision.None;
        }

        int fallback = Math.Min(enemy.Actions.Length - 1, BattleTableFallback);
        for (int i = 0; i < fallback; i++)
        {
            BattleTableAction row = enemy.Actions[i];
            if (TestEnemyCondition(row.Condition, row.ConditionArg, ctx, rng))
            {
                return GetEnemyAction(enemy, row.Target);
            }
        }

        return GetEnemyAction(enemy, enemy.Actions[fallback].Target);
    }

    /// <summary>Selects an eligible attack or technique from the enemy's counter row.</summary>
    /// <param name="enemy">Enemy that was just hit.</param>
    /// <param name="ctx">Battle state exposed to condition evaluation.</param>
    /// <param name="rng">Random source for chance conditions.</param>
    /// <returns>An attack or technique decision, or <see cref="EnemyDecision.None"/> when the counter row is empty (condition 0), its condition fails, or it names a run away or switch, which a counter cannot do.</returns>
    public static EnemyDecision PickCounter(BattleFighter enemy, EnemyAiContext ctx, IBattleRng rng)
    {
        BattleTableAction counter = enemy.Counter;
        if (counter.Condition == 0 || !TestEnemyCondition(counter.Condition, counter.ConditionArg, ctx, rng))
        {
            return EnemyDecision.None;
        }

        EnemyDecision decision = GetEnemyAction(enemy, counter.Target);
        return decision.IsAttack || decision.IsTechnique ? decision : EnemyDecision.None;
    }

    /// <summary>Resolves a switch decision to a living enemy roster slot, or -1 when none qualifies.</summary>
    /// <param name="decision">A switch decision. A fixed slot is used as is, otherwise a living reserve is drawn.</param>
    /// <param name="enemies">Enemy roster, at most three slots are considered.</param>
    /// <param name="activeEnemy">Slot of the enemy currently fighting, never chosen.</param>
    /// <param name="rng">Random source, drawn only when two reserves qualify (one bit picks between them).</param>
    /// <returns>A slot index 0..2 different from <paramref name="activeEnemy"/>, or -1 when the requested slot is dead or no reserve is alive.</returns>
    public static int ResolveSwitch(
        EnemyDecision decision,
        IReadOnlyList<BattleFighter?> enemies,
        int activeEnemy,
        IBattleRng rng)
    {
        if (decision.SwitchSlot >= 0)
        {
            int slot = decision.SwitchSlot;
            return slot != activeEnemy && BattleRoster.IsLiving(enemies[slot]) ? slot : -1;
        }

        Span<int> candidates = stackalloc int[3];
        int count = 0;
        for (int i = 0; i < 3 && i < enemies.Count; i++)
        {
            if (i != activeEnemy && BattleRoster.IsLiving(enemies[i]))
            {
                candidates[count++] = i;
            }
        }

        return count switch
        {
            0 => -1,
            1 => candidates[0],
            _ => candidates[rng.NextValue() & 1],
        };
    }

    /// <summary>Converts a threshold expressed in 128ths into HP, with the same integer truncation as the original rule (percent first, then hundredths of maximum HP), so thresholds hit the same boundary values.</summary>
    /// <param name="maximum">Maximum HP of the fighter.</param>
    /// <param name="fraction">Share of maximum HP in 128ths, 0..128.</param>
    /// <returns>The HP threshold, rounded down at each step.</returns>
    private static int HpShare(int maximum, int fraction) =>
        unchecked((maximum / 100) * (fraction * 100 / 128));

    /// <summary>Tests condition 10: whether a reserve enemy (not the active one) is alive and is the requested Digimon.</summary>
    /// <param name="ctx">Battle state holding the enemy roster and the active slot.</param>
    /// <param name="requestedId">Digimon id from the condition argument, 0 for any living reserve.</param>
    /// <returns><see langword="true"/> when at least one of the first three slots matches.</returns>
    private static bool HasMatchingReserve(EnemyAiContext ctx, int requestedId)
    {
        for (int i = 0; i < 3 && i < ctx.Enemies.Count; i++)
        {
            if (i != ctx.ActiveEnemy && IsRequestedReserve(ctx.Enemies[i], requestedId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a reserve slot holds a fighter that has not been knocked out (HP not 0) and is the
    /// requested Digimon; a requested id of 0 accepts any fighter.
    /// </summary>
    /// <param name="candidate">The reserve slot's content.</param>
    /// <param name="requestedId">The Digimon id the condition asks for, 0 for any.</param>
    /// <returns><see langword="true"/> when the slot satisfies the condition.</returns>
    private static bool IsRequestedReserve(BattleFighter? candidate, int requestedId) =>
        candidate is { Id: not 0, Hp: not 0 } && (requestedId == 0 || candidate.Id == requestedId);
}
