namespace Dw3Production.Simulation;

/// <summary>The battle's presentation phases. The menu shows in PlayerCommand; the
/// enemy's turn is EnemyTurn; Ended is any terminal outcome (won/lost/fled).</summary>
public enum BattleFlowPhase
{
    PlayerCommand,
    EnemyTurn,
    Ended,
}
