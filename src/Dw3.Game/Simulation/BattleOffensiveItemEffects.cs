namespace Dw3Production.Simulation;

/// <summary>Applies the six source-defined enemy-targeted battle items without touching bag stock.</summary>
internal static class BattleOffensiveItemEffects
{
    /// <summary>Applies one qualified offensive item when it can change the living active enemy.</summary>
    /// <param name="state">Battle state containing the attacker, target and scheduler.</param>
    /// <param name="item">Source-classified item from the player bag.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether the item changed battle state and may consume stock.</returns>
    public static bool TryApply(BattleState state, BattleItem item, IBattleRng rng) => item.Effect switch
    {
        "drain" when item.Id == 0x55 => TryDrain(state, item),
        "confuse" when item.Id == 0x56 => TryConfuse(state, item, rng),
        "weakenDefense" when item.Id == 0x57 => TryWeakenDefense(state, item, rng),
        "lowerStat" when item.Id == 0x58 => TryLowerHigherStat(state, item, rng),
        "lowerSpeed" when item.Id == 0x59 => TryLowerSpeed(state, item, rng),
        "itemTech" when item.Id == 0x5A => TryItemTechnique(state, item, rng),
        _ => false,
    };

    /// <summary>Drains exactly 20 percent of the enemy maximum HP when the encounter permits draining.</summary>
    /// <param name="state">Battle state containing both active fighters.</param>
    /// <param name="item">Drain item whose target must be the enemy.</param>
    /// <returns>Whether the item transferred a positive nonlethal damage amount.</returns>
    private static bool TryDrain(BattleState state, BattleItem item)
    {
        if (!HasEnemyTarget(state, item) || state.Blocks[BattleConst.BlockDrain] != 0)
        {
            return false;
        }

        BattleFighter enemy = ActiveEnemy(state);
        BattleFighter player = ActivePlayer(state);
        int damage = enemy.MaxHp / 5;
        if (damage <= 0 || enemy.Hp <= damage)
        {
            return false;
        }

        enemy.Hp -= damage;
        player.Hp = Math.Min(player.Hp + damage, player.MaxHp);
        return true;
    }

    /// <summary>Applies source enemy confusion and its independent partner-backfire chance through the status scheduler.</summary>
    /// <param name="state">Battle state containing the enemy target.</param>
    /// <param name="item">Confusion item whose target must be the enemy.</param>
    /// <param name="rng">Shared random stream for the source chance and status duration.</param>
    /// <returns>Whether the enemy confusion state was applied.</returns>
    private static bool TryConfuse(BattleState state, BattleItem item, IBattleRng rng)
    {
        if (!HasEnemyTarget(state, item) || state.Blocks[BattleConst.BlockConfusion] != 0)
        {
            return false;
        }

        BattleFighter enemy = ActiveEnemy(state);
        if ((enemy.Flags & FighterFlags.Confused) != 0 || (rng.NextValue() & 1) == 0)
        {
            return false;
        }

        if (!state.ApplyDirectItemConfusion(BattleSide.Enemy, item.Amount))
        {
            return false;
        }

        if ((rng.NextValue() & 3) == 0)
        {
            _ = state.ApplyDirectItemConfusion(BattleSide.Player, item.Amount);
        }

        return true;
    }

    /// <summary>Raises enemy attack by three tenths and lowers defense by one half through the scheduler.</summary>
    /// <param name="state">Battle state containing the enemy target.</param>
    /// <param name="item">Defense-weakening item whose target must be the enemy.</param>
    /// <param name="rng">Shared random stream for modifier expiry timing.</param>
    /// <returns>Whether either source stat modifier changed the enemy.</returns>
    private static bool TryWeakenDefense(BattleState state, BattleItem item, IBattleRng rng)
    {
        if (!HasEnemyTarget(state, item) || state.Blocks[BattleConst.BlockLowerDefense] != 0)
        {
            return false;
        }

        BattleFighter enemy = ActiveEnemy(state);
        enemy.RefreshStats();
        int attack = enemy.Stats.Stats[BattleStat.Attack];
        int defense = enemy.Stats.Stats[BattleStat.Defense];
        bool attackChanged = state.ApplyStatDelta(BattleSide.Enemy, state.ActiveEnemy, BattleStat.Attack, attack * 3 / 10, rng);
        bool defenseChanged = state.ApplyStatDelta(BattleSide.Enemy, state.ActiveEnemy, BattleStat.Defense, -(defense / 2), rng);
        return attackChanged || defenseChanged;
    }

    /// <summary>Lowers the strictly higher enemy attack or defense until it meets the other resolved stat.</summary>
    /// <param name="state">Battle state containing the enemy target.</param>
    /// <param name="item">Higher-stat lowering item whose target must be the enemy.</param>
    /// <param name="rng">Shared random stream for modifier expiry timing.</param>
    /// <returns>Whether the selected higher stat changed.</returns>
    private static bool TryLowerHigherStat(BattleState state, BattleItem item, IBattleRng rng)
    {
        if (!HasEnemyTarget(state, item))
        {
            return false;
        }

        BattleFighter enemy = ActiveEnemy(state);
        enemy.RefreshStats();
        int attack = enemy.Stats.Stats[BattleStat.Attack];
        int defense = enemy.Stats.Stats[BattleStat.Defense];
        if (attack > defense && state.Blocks[BattleConst.BlockLowerAttack] == 0)
        {
            return state.ApplyStatDelta(BattleSide.Enemy, state.ActiveEnemy, BattleStat.Attack, defense - attack, rng);
        }

        return defense > attack && state.Blocks[BattleConst.BlockLowerDefense] == 0
            && state.ApplyStatDelta(BattleSide.Enemy, state.ActiveEnemy, BattleStat.Defense, attack - defense, rng);
    }

    /// <summary>Lowers enemy speed by the item's decoded 128th amount when speed lowering is permitted.</summary>
    /// <param name="state">Battle state containing the enemy target.</param>
    /// <param name="item">Speed-lowering item whose amount is measured in 128ths.</param>
    /// <param name="rng">Shared random stream for modifier expiry timing.</param>
    /// <returns>Whether the speed boost storage changed.</returns>
    private static bool TryLowerSpeed(BattleState state, BattleItem item, IBattleRng rng) =>
        HasEnemyTarget(state, item)
        && state.Blocks[BattleConst.BlockLowerSpeed] == 0
        && state.ApplyStatModifier(BattleSide.Enemy, state.ActiveEnemy, BattleStat.Speed, -item.Amount, rng);

    /// <summary>Runs the loaded source item technique 0x89 as direct magic damage without paying MP.</summary>
    /// <param name="state">Battle state containing the attacker, target and technique catalogue.</param>
    /// <param name="item">Technique item whose target must be the enemy.</param>
    /// <param name="rng">Shared battle random stream for the source magic critical policy.</param>
    /// <returns>Whether a positive magic damage value was applied.</returns>
    private static bool TryItemTechnique(BattleState state, BattleItem item, IBattleRng rng)
    {
        if (!HasEnemyTarget(state, item) || !state.Techs.TryGetValue(0x89, out TechData? tech))
        {
            return false;
        }

        BattleFighter player = ActivePlayer(state);
        BattleFighter enemy = ActiveEnemy(state);
        int damage = state.Hits(rng).DirectItemMagic(tech, player, enemy);
        if (damage <= 0)
        {
            return false;
        }

        enemy.TakeDamage(damage);
        state.CheckOutcome();
        state.ResolveEnemyReserve();
        return true;
    }

    /// <summary>Gets the validated active player after an item has passed the shared target guard.</summary>
    /// <param name="state">Battle state already validated by <see cref="HasEnemyTarget"/>.</param>
    /// <returns>The living active player.</returns>
    private static BattleFighter ActivePlayer(BattleState state) => state.ActivePlayerFighter
        ?? throw new InvalidOperationException("Offensive item requires an active player.");

    /// <summary>Gets the validated active enemy after an item has passed the shared target guard.</summary>
    /// <param name="state">Battle state already validated by <see cref="HasEnemyTarget"/>.</param>
    /// <returns>The living active enemy.</returns>
    private static BattleFighter ActiveEnemy(BattleState state) => state.ActiveEnemyFighter
        ?? throw new InvalidOperationException("Offensive item requires an active enemy.");

    /// <summary>Validates the source enemy target and both living active fighters before a random draw or mutation.</summary>
    /// <param name="state">Battle state containing both active sides.</param>
    /// <param name="item">Candidate offensive item.</param>
    /// <returns>Whether the source target and active fighters are valid.</returns>
    private static bool HasEnemyTarget(BattleState state, BattleItem item) =>
        item.Target == "enemy"
        && state.ActivePlayerFighter is { IsKnockedOut: false }
        && state.ActiveEnemyFighter is { IsKnockedOut: false };
}
