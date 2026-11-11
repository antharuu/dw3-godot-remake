namespace Dw3Production.Simulation;

/// <summary>The battle's presentation phases. The menu shows in PlayerCommand; the
/// enemy's turn is EnemyTurn; Ended is any terminal outcome (won/lost/fled).</summary>
public enum BattleFlowPhase
{
    /// <summary>The player picks a command. Only this phase accepts <see cref="BattleFlowController.Choose"/>.</summary>
    PlayerCommand,

    /// <summary>The active enemy acts. Only this phase accepts <see cref="BattleFlowController.EnemyAct"/>.</summary>
    EnemyTurn,

    /// <summary>An action-script timeline is running. No command input is accepted; the host ticks the sequence.</summary>
    ActionSequence,

    /// <summary>The battle has an outcome and no more turns are played.</summary>
    Ended,
}
