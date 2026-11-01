namespace Dw3Production.Simulation;

/// <summary>Actions that an enemy battle-table row can select.</summary>
public enum EnemyActionKind
{
    /// <summary>No action.</summary>
    None,
    /// <summary>Use the normal attack technique.</summary>
    Attack,
    /// <summary>Use the first special technique.</summary>
    Technique1,
    /// <summary>Use the second special technique.</summary>
    Technique2,
    /// <summary>Attempt to leave the battle.</summary>
    RunAway,
    /// <summary>Switch to enemy roster slot zero.</summary>
    SwitchTo0,
    /// <summary>Switch to enemy roster slot one.</summary>
    SwitchTo1,
    /// <summary>Switch to enemy roster slot two.</summary>
    SwitchTo2,
    /// <summary>Switch to any eligible enemy roster slot.</summary>
    SwitchToAny,
}

/// <summary>A resolved enemy action and its optional technique or roster slot.</summary>
public readonly record struct EnemyDecision(EnemyActionKind Kind, int TechId, int SwitchSlot)
{
    /// <summary>Gets the decision that performs no action.</summary>
    public static EnemyDecision None => new(EnemyActionKind.None, 0, -1);

    /// <summary>Gets whether this is a normal attack.</summary>
    public bool IsAttack => Kind == EnemyActionKind.Attack;

    /// <summary>Gets whether this uses either special technique.</summary>
    public bool IsTechnique => Kind is EnemyActionKind.Technique1 or EnemyActionKind.Technique2;

    /// <summary>Gets whether this requests an enemy switch.</summary>
    public bool IsSwitch => Kind is >= EnemyActionKind.SwitchTo0 and <= EnemyActionKind.SwitchToAny;

    /// <summary>Gets whether this requests retreat from battle.</summary>
    public bool IsRunAway => Kind == EnemyActionKind.RunAway;
}

/// <summary>Battle state consulted while evaluating an enemy action table.</summary>
public sealed class EnemyAiContext
{
    /// <summary>The enemy whose action is being selected.</summary>
    public BattleFighter Enemy = new();

    /// <summary>The active player fighter, or <see langword="null"/> when absent.</summary>
    public BattleFighter? ActivePlayer;

    /// <summary>The enemy roster, where a null entry is an empty slot.</summary>
    public IReadOnlyList<BattleFighter?> Enemies = [];

    /// <summary>The roster index of <see cref="Enemy"/>.</summary>
    public int ActiveEnemy;

    /// <summary>The element currently boosted by the battlefield.</summary>
    public int BoostElement;

    /// <summary>The current battle identifier.</summary>
    public int BattleId;

    /// <summary>The current encounter category.</summary>
    public int EncounterKind;

    /// <summary>Digimon identifiers treated as starters by table conditions.</summary>
    public IReadOnlyCollection<int> StarterDigimonIds = [];
}

/// <summary>Evaluates enemy action tables and resolves their battle decisions.</summary>
public static class BattleEnemyAi
{
    /// <summary>The conventional fallback row in a four-row action table.</summary>
    public const int BattleTableFallback = 3;

    /// <summary>Evaluates one action-table condition against the current battle state.</summary>
    public static bool TestEnemyCondition(int condition, int arg, EnemyAiContext ctx, BattleRng rng)
    {
        BattleFighter enemy = ctx.Enemy;
        BattleFighter? player = ctx.ActivePlayer;

        return condition switch
        {
            0 => true,
            1 => rng.Next() % 128 < arg,
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
    public static EnemyDecision PickAction(BattleFighter enemy, EnemyAiContext ctx, BattleRng rng)
    {
        if (enemy.Actions.Length == 0) return EnemyDecision.None;

        int fallback = Math.Min(enemy.Actions.Length - 1, BattleTableFallback);
        for (int i = 0; i < fallback; i++)
        {
            BattleTableAction row = enemy.Actions[i];
            if (TestEnemyCondition(row.Condition, row.ConditionArg, ctx, rng))
                return GetEnemyAction(enemy, row.Target);
        }

        return GetEnemyAction(enemy, enemy.Actions[fallback].Target);
    }

    /// <summary>Selects an eligible attack or technique from the enemy's counter row.</summary>
    public static EnemyDecision PickCounter(BattleFighter enemy, EnemyAiContext ctx, BattleRng rng)
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
        BattleRng rng)
    {
        if (decision.SwitchSlot >= 0)
        {
            int slot = decision.SwitchSlot;
            if (slot == activeEnemy) return -1;
            return IsAlive(enemies[slot]) ? slot : -1;
        }

        Span<int> candidates = stackalloc int[3];
        int count = 0;
        for (int i = 0; i < 3 && i < enemies.Count; i++)
        {
            if (i != activeEnemy && IsAlive(enemies[i])) candidates[count++] = i;
        }

        return count switch
        {
            0 => -1,
            1 => candidates[0],
            _ => candidates[rng.Next() & 1],
        };
    }

    private static int HpShare(int maximum, int fraction) =>
        unchecked((maximum / 100) * (fraction * 100 / 128));

    private static bool HasMatchingReserve(EnemyAiContext ctx, int requestedId)
    {
        for (int i = 0; i < 3 && i < ctx.Enemies.Count; i++)
        {
            if (i == ctx.ActiveEnemy) continue;
            BattleFighter? candidate = ctx.Enemies[i];
            if (candidate is not null && candidate.Id != 0 && candidate.Hp != 0 &&
                (requestedId == 0 || candidate.Id == requestedId))
                return true;
        }

        return false;
    }

    private static bool IsAlive(BattleFighter? fighter) =>
        fighter is not null && fighter.Id != 0 && fighter.Hp > 0;
}
