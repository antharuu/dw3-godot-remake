namespace Dw3.Formats.Battle;

/// <summary>One battle action field from the on-disc battle table.</summary>
public sealed record BattleAction(byte Target, byte Condition, short ConditionArgument);
