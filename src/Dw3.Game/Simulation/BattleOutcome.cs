namespace Dw3Production.Simulation;

/// <summary>How a battle ends, plus <see cref="None"/> while it is running.</summary>
public enum BattleOutcome
{
    /// <summary>The battle is still running.</summary>
    None,

    /// <summary>One side ran away. The player returns to the field with no reward and no penalty.</summary>
    Fled,

    /// <summary>Every enemy is knocked out. The fight report then grants experience, money and items.</summary>
    Won,

    /// <summary>Every partner is knocked out. The game returns to the title screen.</summary>
    Lost,
}
