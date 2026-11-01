namespace Dw3Production.Simulation;

/// <summary>Evaluates enemy action tables and resolves their battle decisions.</summary>
public static class BattleEnemyAi
{
    /// <summary>The conventional fallback row in a four-row action table.</summary>
    public const int BattleTableFallback = 3;

    /// <summary>Evaluates one action-table condition against the current battle state.</summary>
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

    private static int HpShare(int maximum, int fraction) =>
        unchecked((maximum / 100) * (fraction * 100 / 128));

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
