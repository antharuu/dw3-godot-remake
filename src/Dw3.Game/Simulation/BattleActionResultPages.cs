namespace Dw3Production.Simulation;

/// <summary>
/// Builds the result messages of a finished action from its frozen hit events, the way the source's technique,
/// enemy-attack and counterattack tasks do once the script ends: the main target's damage, multi-hit damage,
/// or miss, then the counterattack's own result. A knock-out effect shows no result page.
/// </summary>
public static class BattleActionResultPages
{
    /// <summary>Message key of a heal of the user's active fighter (source message type 8).</summary>
    public const string RecoveredKey = "recovered";

    /// <summary>Message key of a heal of the user's whole side, naming each hurt fighter (source message type 9).</summary>
    public const string SideRecoveredKey = "sideRecovered";

    /// <summary>Message key of a heal that restored nothing.</summary>
    public const string NoEffectKey = "noEffect";

    /// <summary>Builds the result pages of an action.</summary>
    /// <param name="context">Frozen action context with its ordered hit events.</param>
    /// <returns>The pages to show in order, empty when the action has no damage resolution.</returns>
    public static IReadOnlyList<BattleActionResultPage> Build(ActionSequenceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Recovery is { } recovery)
        {
            return [RecoveryPage(recovery)];
        }

        var pages = new List<BattleActionResultPage>();
        BattleHitEvent[] main = [.. context.HitEvents.Where(hit => !hit.IsCounter)];
        BattleSide defender = Opposite(context.AttackerSide);
        if (main.Length > 0 && main[0].Effect != BattleTechniqueEffect.KnockOut)
        {
            pages.Add(MainPage(main, defender, context.TargetId));
        }

        BattleHitEvent[] counter = [.. context.HitEvents.Where(hit => hit.IsCounter)];
        if (counter.Length > 0)
        {
            pages.Add(SingleHitPage(counter[0], context.AttackerSide, context.AttackerId));
        }

        return pages;
    }

    /// <summary>
    /// Builds the page of the action's own hits: a double magic prints the sum of its hits' damage (type 4); any
    /// other multi-hit action (multi-hit effect or the triple-hit equipment) prints the per-hit damage, the landed
    /// hits and their product (type 0x10); a single hit prints its damage or the miss.
    /// </summary>
    /// <param name="hits">Ordered hits of the action, counters excluded.</param>
    /// <param name="side">Side of the target.</param>
    /// <param name="target">Target fighter id.</param>
    /// <returns>The page.</returns>
    private static BattleActionResultPage MainPage(BattleHitEvent[] hits, BattleSide side, int target)
    {
        BattleHitEvent[] landed = [.. hits.Where(hit => hit.Landed)];
        if (hits[0].Effect == BattleTechniqueEffect.DoubleMagic)
        {
            return new BattleActionResultPage("damage", side, target, [landed.Sum(hit => hit.FormulaDamage)]);
        }

        if (hits[0].Effect == BattleTechniqueEffect.MultiHit || hits.Length > 1)
        {
            int perHit = landed.Length == 0 ? 0 : landed.Max(hit => hit.FormulaDamage);
            return new BattleActionResultPage("multiHitDamage", side, target, [perHit, landed.Length, perHit * landed.Length]);
        }

        return SingleHitPage(hits[0], side, target);
    }

    /// <summary>Builds the damage page of one landed hit, or the miss page.</summary>
    /// <param name="hit">The hit.</param>
    /// <param name="side">Side of the target.</param>
    /// <param name="target">Target fighter id.</param>
    /// <returns>The page.</returns>
    private static BattleActionResultPage SingleHitPage(BattleHitEvent hit, BattleSide side, int target) => hit.Landed
        ? new BattleActionResultPage("damage", side, target, [hit.FormulaDamage])
        : new BattleActionResultPage("missed", side, target, []);

    /// <summary>
    /// Builds the page of a healing technique: the healed active fighter and its HP, the hurt fighters of the side
    /// and the HP printed for them, or the user and the no-effect line.
    /// </summary>
    /// <param name="recovery">Frozen heal result.</param>
    /// <returns>The page.</returns>
    private static BattleActionResultPage RecoveryPage(BattleRecoveryOutcome recovery)
    {
        if (!recovery.Healed)
        {
            return new BattleActionResultPage(NoEffectKey, recovery.Side, recovery.NamedIds[0], []);
        }

        string key = recovery.SideWide ? SideRecoveredKey : RecoveredKey;
        return new BattleActionResultPage(key, recovery.Side, recovery.NamedIds[0], [recovery.Amount]) { NamedIds = recovery.NamedIds };
    }

    /// <summary>Returns the other side.</summary>
    /// <param name="side">One side.</param>
    /// <returns>The opposing side.</returns>
    private static BattleSide Opposite(BattleSide side) => side == BattleSide.Player ? BattleSide.Enemy : BattleSide.Player;
}
