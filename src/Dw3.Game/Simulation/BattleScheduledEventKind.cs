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

    /// <summary>Recurring poison damage attached to one fighter.</summary>
    PoisonDamage,

    /// <summary>Expiry of one fighter's paralysis.</summary>
    ParalysisEnd,

    /// <summary>Expiry of one fighter's confusion.</summary>
    ConfusionEnd,

    /// <summary>Expiry of one fighter's sleep.</summary>
    SleepEnd,

    /// <summary>Expiry of one fighter's temporary attack modifier.</summary>
    AttackBoostEnd,

    /// <summary>Expiry of one fighter's temporary defense modifier.</summary>
    DefenseBoostEnd,

    /// <summary>Expiry of one fighter's temporary speed modifier.</summary>
    SpeedBoostEnd,
}
