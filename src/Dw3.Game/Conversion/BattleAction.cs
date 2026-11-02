namespace Dw3Production.Conversion;

/// <summary>One enemy battle-table action (target, condition, condition argument).</summary>
public sealed record BattleAction(int Target, int Condition, int ConditionArg);
