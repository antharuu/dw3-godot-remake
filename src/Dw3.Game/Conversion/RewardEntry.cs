namespace Dw3Production.Conversion;

/// <summary>One battle's reward (BattleReward: digimonExp, exp, money).</summary>
public sealed record RewardEntry(int Index, int DigimonExp, int Exp, int Money);
