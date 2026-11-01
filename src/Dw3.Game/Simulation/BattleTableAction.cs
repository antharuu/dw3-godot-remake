namespace Dw3Production.Simulation;

/// <summary>One enemy battle-table action (battle.h BattleTableAction): its target kind
/// (FIGHTSTG_getEnemyAction), the condition that must hold, and the condition's argument.
/// A value type like the source's 4-byte struct, so a default action is (0,0,0) = none.</summary>
public readonly record struct BattleTableAction(int Target, int Condition, int ConditionArg);
