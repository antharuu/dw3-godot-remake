namespace Dw3Production.Simulation;

/// <summary>How a battle ends (battle.h BATTLE_FLED/WON/LOST, plus None while running).</summary>
public enum BattleOutcome
{
    None,
    Fled,
    Won,
    Lost,
}
