namespace Dw3Production.Simulation;

/// <summary>How a battle ends, plus <see cref="None"/> while it is running.</summary>
public enum BattleOutcome
{
    /// <summary>The battle is still running.</summary>
    None,

    /// <summary>The player ran away. The party returns to the field with no reward or penalty.</summary>
    PlayerFled,

    /// <summary>The enemy ran away. The party returns to the field with no reward or penalty.</summary>
    EnemyFled,

    /// <summary>Every enemy is knocked out. The fight report then grants experience, money and items.</summary>
    Won,

    /// <summary>Every partner is knocked out. The game returns to the title screen.</summary>
    Lost,
}
