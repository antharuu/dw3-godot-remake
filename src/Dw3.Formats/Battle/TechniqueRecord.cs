namespace Dw3.Formats.Battle;

/// <summary>One fixed-size technique record from the boot executable.</summary>
public sealed record TechniqueRecord(
    ushort Mp,
    ushort Power,
    byte Icon,
    byte Kind,
    byte Accuracy,
    byte Element,
    byte ElementPower,
    byte Family,
    byte Effect,
    byte EffectChance,
    byte EffectPower,
    byte ScriptStage,
    byte ScriptEffect,
    byte ScriptSound,
    byte Script,
    byte HitCount);
