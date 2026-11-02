using Dw3Production.Conversion;
using Dw3Production.Presentation;

namespace Dw3Production.Simulation;

/// <summary>Projects mutable battle state into the renderer's immutable frame view.</summary>
public static class BattleViewBuilder
{
    /// <summary>Builds one battle presentation frame.</summary>
    /// <param name="flow">Active battle flow.</param>
    /// <param name="menu">Current command-menu view.</param>
    /// <param name="report">Resolved victory report, when available.</param>
    /// <param name="nameFor">Resolves decoded display text for a fighter id.</param>
    /// <returns>The complete battle frame.</returns>
    public static BattleView Build(
        BattleFlowController flow,
        BattleMenuView menu,
        BattleReportView? report,
        Func<int, BattleTextLine> nameFor)
    {
        List<BattleFighterView> players = BuildFighters(
            flow.State.Players,
            flow.State.ActivePlayer,
            nameFor);
        List<BattleFighterView> enemies = BuildFighters(
            flow.State.Enemies,
            flow.State.ActiveEnemy,
            nameFor);
        BattleMenuView? visibleMenu = flow.Phase == BattleFlowPhase.PlayerCommand ? menu : null;
        return new BattleView(
            flow.Phase,
            flow.Outcome,
            players,
            enemies,
            flow.Message,
            visibleMenu,
            report,
            flow.MessageText,
            flow.MessageVisible);
    }

    /// <summary>Projects one side's fixed fighter slots.</summary>
    /// <param name="fighters">Fighter slots.</param>
    /// <param name="activeIndex">Active slot index.</param>
    /// <param name="nameFor">Decoded-name resolver.</param>
    /// <returns>Presentation fighter slots preserving empty entries.</returns>
    private static List<BattleFighterView> BuildFighters(
        BattleFighter?[] fighters,
        int activeIndex,
        Func<int, BattleTextLine> nameFor)
    {
        var result = new List<BattleFighterView>(fighters.Length);
        for (int index = 0; index < fighters.Length; index++)
        {
            result.Add(BuildFighter(fighters[index], index == activeIndex, nameFor));
        }

        return result;
    }

    /// <summary>Projects one occupied or empty battle slot.</summary>
    /// <param name="fighter">Fighter occupying the slot.</param>
    /// <param name="active">Whether this slot currently acts.</param>
    /// <param name="nameFor">Decoded-name resolver.</param>
    /// <returns>The fighter presentation record.</returns>
    private static BattleFighterView BuildFighter(
        BattleFighter? fighter,
        bool active,
        Func<int, BattleTextLine> nameFor) => fighter is null
            ? new BattleFighterView(0, null, 0, 0, 0, 0, false, false)
            : new BattleFighterView(
                fighter.Id,
                nameFor(fighter.Id),
                fighter.Hp,
                fighter.MaxHp,
                fighter.Mp,
                fighter.MaxMp,
                active,
                fighter.IsKnockedOut);
}
