namespace Dw3Production.Simulation;

/// <summary>Contains the experience and money awarded by a completed battle.</summary>
public readonly record struct BattleReward(int DigimonExp, int Exp, int Money);
