namespace Dw3Production.Simulation;

/// <summary>Identifies the executable class of a scheduled battle event.</summary>
public enum BattleScheduledEventKind
{
    /// <summary>A player command turn.</summary>
    PlayerTurn,

    /// <summary>An enemy action turn.</summary>
    EnemyTurn,

    /// <summary>An immediate interrupt that precedes ordinary turns.</summary>
    Interrupt,
}
