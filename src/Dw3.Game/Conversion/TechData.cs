namespace Dw3Production.Conversion;

/// <summary>One TECHS entry (TechData): the fields FIGHTSTG_adjustDamage consumes.</summary>
public sealed record TechData(int Id, int Mp, int Power, int Icon, int Kind, int Accuracy, int Element, int ElementPower, int Family, int Effect, int EffectChance, int EffectPower, int ScriptStage, int ScriptEffect, int ScriptSound, int Script, int HitCount);
