namespace Dw3Production.Simulation;

/// <summary>One action of an enemy's fighter table entry: its target kind
/// (see <see cref="BattleEnemyAi.GetEnemyAction"/>), the condition that must hold, and the condition's argument.
/// A value type like the 4-byte record on the disc, so a default action is (0,0,0) = none.</summary>
public readonly record struct BattleTableAction(int Target, int Condition, int ConditionArg);
