namespace Dw3Production.Simulation;

/// <summary>Calculates poison application and periodic damage from the battle-state contract.</summary>
public static class BattlePoisonRules
{
    /// <summary>Gets the battle side opposite the supplied side.</summary>
    /// <param name="side">Known battle side.</param>
    /// <returns>The other side.</returns>
    public static BattleSide Opposite(BattleSide side) =>
        side == BattleSide.Player ? BattleSide.Enemy : BattleSide.Player;

    /// <summary>Evaluates a poison application roll using wisdom and both source resistance channels.</summary>
    /// <param name="baseChance">Technique or passive base chance.</param>
    /// <param name="attacker">Fighter applying poison.</param>
    /// <param name="target">Fighter resisting poison.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether the application roll succeeded.</returns>
    public static bool RollSucceeds(int baseChance, BattleFighter attacker, BattleFighter target, IBattleRng rng)
    {
        int resistance = target.Stats.Resist[Resist.Poison] + target.Stats.Resist[BattleConst.PoisonElementResistance];
        int chance = baseChance + (attacker.Stats.Stats[BattleStat.Wisdom] / 8) - ((resistance + (target.Stats.Stats[BattleStat.Wisdom] / 2)) / 8);
        return (rng.NextValue() & 0x7F) < Math.Clamp(chance, 1, 127);
    }

    /// <summary>Calculates periodic poison damage as a bounded fraction of the affected fighter's maximum HP.</summary>
    /// <param name="power">Stored poison strength.</param>
    /// <param name="fighter">Affected fighter with refreshed resistances.</param>
    /// <returns>At least one and at most half the fighter's maximum HP.</returns>
    public static int Damage(int power, BattleFighter fighter)
    {
        int resistance = fighter.Stats.Resist[Resist.Poison] + fighter.Stats.Resist[BattleConst.PoisonElementResistance];
        int raw = ((power / 2) - (resistance / 10)) * fighter.MaxHp / 100;
        return Math.Clamp(raw, 1, fighter.MaxHp / 2);
    }
}
