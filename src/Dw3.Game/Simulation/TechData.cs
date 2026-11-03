namespace Dw3Production.Simulation;

/// <summary>A technique as the damage math reads it, copied from the technique table of the pack.</summary>
public sealed class TechData
{
    /// <summary>Gets or sets the MP cost. The technique is refused when the user has less MP, unless it is in a temporary form.</summary>
    public int Mp { get; set; }

    /// <summary>Gets or sets the base power, multiplied by attack / defense (physical) or scaled by the spirit ratio (magic).</summary>
    public int Power { get; set; }

    /// <summary>Gets or sets the icon kind: <see cref="BattleConst.TechMagic"/> selects the magic formula, any other value the physical one.</summary>
    public int Icon { get; set; }

    /// <summary>Gets or sets the technique kind. Kind 3 heals the target instead of damaging it.</summary>
    public int Kind { get; set; }

    /// <summary>Gets or sets the accuracy value of the technique.</summary>
    public int Accuracy { get; set; }

    /// <summary>Gets or sets the element id, below <see cref="BattleConst.ElementFirst"/> for a non-elemental technique.</summary>
    public int Element { get; set; }

    /// <summary>Gets or sets the element power, which drives the elemental bonus and the magic critical chance.</summary>
    public int ElementPower { get; set; }

    /// <summary>Gets or sets the family id. A match with the target's family adds 50 % damage.</summary>
    public int Family { get; set; }

    /// <summary>Gets or sets the side effect id, below <see cref="BattleConst.TechEffectFirst"/> for none. <see cref="BattleConst.TechEffectCritical"/> raises the critical chance.</summary>
    public int Effect { get; set; }

    /// <summary>Gets or sets the chance that the side effect applies.</summary>
    public int EffectChance { get; set; }

    /// <summary>Gets or sets the strength of the side effect, for example the critical bonus or, for a counter, the share of the received damage in 64ths.</summary>
    public int EffectPower { get; set; }

    /// <summary>Gets or sets the battle stage script used to play the technique's animation.</summary>
    public int ScriptStage { get; set; }

    /// <summary>Gets or sets the visual effect played by the technique.</summary>
    public int ScriptEffect { get; set; }

    /// <summary>Gets or sets the sound played by the technique.</summary>
    public int ScriptSound { get; set; }

    /// <summary>Gets or sets the script id of the technique. Scripts 5 and 12 are special, and scripts 11 and 12 ignore the accessory element and the triple hit.</summary>
    public int Script { get; set; }

    /// <summary>Gets or sets the number of hits the technique deals.</summary>
    public int HitCount { get; set; }
}
