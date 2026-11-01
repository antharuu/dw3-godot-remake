namespace Dw3Production.Simulation;

/// <summary>A technique (game_state.h TechData), the data the damage math reads.</summary>
public sealed class TechData
{
    public int Mp { get; set; }

    public int Power { get; set; }

    public int Icon { get; set; } // BattleConst.TechPhysical / TechMagic

    public int Kind { get; set; } // 3 = heals the target

    public int Accuracy { get; set; }

    public int Element { get; set; }

    public int ElementPower { get; set; }

    public int Family { get; set; }

    public int Effect { get; set; }

    public int EffectChance { get; set; }

    public int EffectPower { get; set; }

    public int ScriptStage { get; set; }

    public int ScriptEffect { get; set; }

    public int ScriptSound { get; set; }

    public int Script { get; set; } // 5 and 12 are special

    public int HitCount { get; set; }
}
