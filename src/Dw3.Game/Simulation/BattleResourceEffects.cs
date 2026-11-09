namespace Dw3Production.Simulation;

/// <summary>Resolves HP draining, player theft and enemy MP draining after their action gate.</summary>
public static class BattleResourceEffects
{
    /// <summary>Applies a landed HP drain.</summary>
    /// <param name="state">Battle state.</param>
    /// <param name="side">Attacker side.</param>
    /// <param name="chance">Chance in 128ths.</param>
    /// <param name="power">Share in 128ths.</param>
    /// <param name="resolvedDamage">Formula damage before HP clamping, used as the base for the heal even when the hit was lethal.</param>
    /// <param name="rng">Battle RNG.</param>
    /// <returns>Whether the drain roll succeeded.</returns>
    public static bool DrainHp(BattleState state, BattleSide side, int chance, int power, int resolvedDamage, IBattleRng rng)
    {
        BattleFighter? attacker = state.FighterFor(side);
        BattleFighter? target = state.FighterFor(BattlePoisonRules.Opposite(side));
        if ((side == BattleSide.Player && state.Blocks[BattleConst.BlockDrain] != 0)
            || attacker is not { IsKnockedOut: false }
            || target is null
            || (rng.NextValue() & 127) >= chance)
        {
            return false;
        }

        attacker.Hp = Math.Min(attacker.Hp + (resolvedDamage * (attacker.Special ? power * 2 : power) / 128), attacker.MaxHp);
        return true;
    }

    /// <summary>Attempts a player item theft.</summary>
    /// <param name="state">Battle state.</param>
    /// <param name="side">Attacker side.</param>
    /// <param name="chance">Technique chance.</param>
    /// <param name="rng">Battle RNG.</param>
    /// <returns>Whether an item transferred.</returns>
    public static bool Steal(BattleState state, BattleSide side, int chance, IBattleRng rng)
    {
        BattleFighter? attacker = state.FighterFor(side);
        BattleFighter? target = state.FighterFor(BattlePoisonRules.Opposite(side));
        if (side != BattleSide.Player
            || state.Blocks[BattleConst.BlockSteal] != 0
            || attacker is not { IsKnockedOut: false }
            || target is not { IsKnockedOut: false }
            || target.Item <= 0)
        {
            return false;
        }

        int ratio = Math.Min(200, attacker.Stats.Stats[BattleStat.Speed] * 100 / Math.Max(target.Stats.Stats[BattleStat.Speed], 1));
        int odds = target.ItemChance * ratio * ((chance + attacker.Stats.StealBonus) * 100 / 64) / 10000;
        if (rng.NextValue() % 1024 >= odds)
        {
            return false;
        }

        state.SetPendingStolenItem(target.Item);
        target.Item = 0;
        return true;
    }

    /// <summary>Transfers MP from partner to enemy.</summary>
    /// <param name="state">Battle state.</param>
    /// <param name="side">Attacker side.</param>
    /// <param name="power">Share in 128ths.</param>
    /// <returns>Whether MP existed.</returns>
    public static bool DrainMp(BattleState state, BattleSide side, int power)
    {
        if (side != BattleSide.Enemy
            || state.ActivePlayerFighter is not { IsKnockedOut: false } from
            || state.ActiveEnemyFighter is not { IsKnockedOut: false } to
            || from.Mp == 0)
        {
            return false;
        }

        int amount = Math.Min(from.Mp, from.MaxMp * power / 128);
        to.Mp = Math.Min(to.Mp + amount, to.MaxMp);
        from.Mp -= amount;
        return true;
    }
}
