namespace Dw3Production.Simulation;

/// <summary>
/// Resolves the HP-healing item techniques (effect <see cref="BattleTechniqueEffect.ItemHeal"/>, ids 0xB8..0xBC) when a
/// fighter uses them as its own technique. No hit is rolled: the heal is 64 HP per point of power plus an eighth of
/// the power per point of the user's wisdom, capped at 9999. Ids below 0xBB heal the user's active fighter, the
/// others every living fighter of its side.
/// </summary>
public static class BattleTechniqueRecovery
{
    /// <summary>First healing technique id of the family.</summary>
    private const int FirstHeal = 0xB8;

    /// <summary>First healing technique id that heals the user's whole side.</summary>
    private const int FirstSideHeal = 0xBB;

    /// <summary>Last healing technique id of the family.</summary>
    private const int LastHeal = 0xBC;

    /// <summary>Largest heal a technique can compute.</summary>
    private const int MaxHeal = 9999;

    /// <summary>Tests whether a technique belongs to the healing family this resolver owns.</summary>
    /// <param name="technique">Technique to test.</param>
    /// <returns>Whether it is an item-icon HP heal with a source healing id.</returns>
    public static bool Supports(TechData technique)
    {
        ArgumentNullException.ThrowIfNull(technique);
        return technique.Icon == BattleConst.TechItem && technique.Effect == BattleTechniqueEffect.ItemHeal && technique.Id is >= FirstHeal and <= LastHeal;
    }

    /// <summary>Computes the HP the user's technique restores from its power and the user's resolved wisdom.</summary>
    /// <param name="technique">Healing technique.</param>
    /// <param name="user">User's resolved battle stats.</param>
    /// <returns>The heal, at most 9999.</returns>
    public static int ComputeHeal(TechData technique, BattleStats user)
    {
        ArgumentNullException.ThrowIfNull(technique);
        ArgumentNullException.ThrowIfNull(user);
        int power = technique.Power;
        return Math.Min((power * 64) + (user.Stats[BattleStat.Wisdom] * power / 8), MaxHeal);
    }

    /// <summary>Heals the user's active fighter or living side and returns the frozen message result.</summary>
    /// <param name="state">Battle receiving the heal.</param>
    /// <param name="side">Side of the user.</param>
    /// <param name="technique">Supported healing technique.</param>
    /// <returns>The fighters named by the result message and the printed amount.</returns>
    public static BattleRecoveryOutcome Apply(BattleState state, BattleSide side, TechData technique)
    {
        ArgumentNullException.ThrowIfNull(state);
        BattleFighter user = state.FighterFor(side) ?? throw new InvalidOperationException("A heal needs a living user.");
        user.RefreshStats();
        int heal = ComputeHeal(technique, user.Stats);
        bool sideWide = technique.Id >= FirstSideHeal;
        BattleFighter?[] sideSlots = side == BattleSide.Player ? state.Players : state.Enemies;
        BattleFighter[] recipients = sideWide ? [.. Living(sideSlots)] : [user];
        BattleFighter[] hurt = [.. recipients.Where(fighter => fighter.Hp < fighter.MaxHp)];
        int amount = Math.Min(heal, hurt.Select(fighter => fighter.MaxHp - fighter.Hp).DefaultIfEmpty(0).Max());
        if (amount <= 0)
        {
            return new BattleRecoveryOutcome(side, sideWide, [user.Id], 0);
        }

        foreach (BattleFighter fighter in recipients)
        {
            fighter.Hp = Math.Min(fighter.Hp + heal, fighter.MaxHp);
        }

        return new BattleRecoveryOutcome(side, sideWide, [.. hurt.Select(fighter => fighter.Id)], amount);
    }

    /// <summary>Gets the existing living fighters of one side in slot order.</summary>
    /// <param name="fighters">Three side slots.</param>
    /// <returns>Every existing fighter with HP left.</returns>
    private static IEnumerable<BattleFighter> Living(IEnumerable<BattleFighter?> fighters) =>
        fighters.OfType<BattleFighter>().Where(fighter => fighter.Id != 0 && !fighter.IsKnockedOut);
}
