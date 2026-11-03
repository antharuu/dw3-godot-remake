namespace Dw3Production.Conversion;

/// <summary>One entry of the technique table: the fields the damage adjustment reads.</summary>
public sealed record TechData(int Id, int Mp, int Power, int Icon, int Kind, int Accuracy, int Element, int ElementPower, int Family, int Effect, int EffectChance, int EffectPower, int ScriptStage, int ScriptEffect, int ScriptSound, int Script, int HitCount);
