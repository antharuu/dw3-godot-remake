namespace Dw3Production.Simulation;

/// <summary>
/// A fighter's resolved battle stats. For an enemy, <see cref="DamageCalculator.ComputeEnemyStats"/>
/// fills it from the raw fighter table entry scaled by strength/16. For a partner it is
/// injected already resolved.
/// </summary>
public sealed class BattleStats
{
    /// <summary>Gets or sets the fighter level used by the damage and growth formulas.</summary>
    public int Level { get; set; }

    /// <summary>Gets or sets the five stats indexed by <see cref="BattleStat"/>: attack, defense, spirit, wisdom, speed.</summary>
    public int[] Stats { get; set; } = new int[5];

    /// <summary>Gets or sets the twelve resistances: 0..6 per element (indexed by element - <see cref="BattleConst.ElementFirst"/>), 7..11 per status. A magic hit is divided by its elemental resistance.</summary>
    public int[] Resist { get; set; } = new int[12];

    /// <summary>Gets or sets the fighter's status bits (<see cref="FighterFlags"/>) as seen by the damage math, copied from the fighter when it is created.</summary>
    public int Flags { get; set; }

    /// <summary>Gets or sets the family id. Ids below <see cref="BattleConst.FamilyFirst"/> mean no family.</summary>
    public int Family { get; set; }

    /// <summary>Gets or sets the fighter's charge added to damage, in 64ths, copied from <see cref="BattleFighter.Charge"/>.</summary>
    public int DamageBonus { get; set; }

    /// <summary>Gets or sets the flat amount taken off each hit received, after the other modifiers. Damage never drops below 0.</summary>
    public int DamageCut { get; set; }

    /// <summary>Gets or sets the families of the three equipped weapons, matched against the defender's family for a critical bonus.</summary>
    public int[] WeaponFamilies { get; set; } = new int[3];

    /// <summary>Gets or sets the element of the equipped accessory, below <see cref="BattleConst.ElementFirst"/> when it has none.</summary>
    public int Element { get; set; }

    /// <summary>Gets or sets the power of the accessory's element.</summary>
    public int ElementPower { get; set; }

    /// <summary>Gets or sets the triple hit value of the equipment. When non-zero, each hit of a plain damaging technique is cut to 4/10 of its damage, the technique hitting three times.</summary>
    public int TripleHit { get; set; }

    /// <summary>Gets or sets the equipment bonus added to the critical hit chance, out of 128.</summary>
    public int CriticalBonus { get; set; }

    /// <summary>Gets or sets the equipment counter-attack value, copied from the equipment and not read by the current battle rules.</summary>
    public int Counter { get; set; }

    /// <summary>Gets or sets the equipment bonus to hit chance, copied from the equipment and not read by the current battle rules.</summary>
    public int Accuracy { get; set; }

    /// <summary>Gets or sets the equipment bonus to dodge chance, copied from the equipment and not read by the current battle rules.</summary>
    public int Evasion { get; set; }

    /// <summary>Gets or sets the bonus added to the fighter's own run away chance, out of 128.</summary>
    public int RunAwayBonus { get; set; }

    /// <summary>Gets or sets the guard against enemies running away. When non-zero, an enemy's base run away chance drops from 64 to 32 out of 128.</summary>
    public int RunAwayGuard { get; set; }

    /// <summary>Gets or sets the equipment bonus to the chance of stealing an item, copied from the equipment and not read by the current battle rules.</summary>
    public int StealBonus { get; set; }

    /// <summary>
    /// Whether one of the three equipped weapon families matches <paramref name="family"/>.
    /// Slots below <see cref="BattleConst.FamilyFirst"/> hold no family and never match.
    /// </summary>
    /// <param name="family">The defender's family.</param>
    /// <returns><see langword="true"/> when a weapon of that family is equipped.</returns>
    public bool HasFamilyWeapon(int family) =>
        Array.Exists(WeaponFamilies, weaponFamily => weaponFamily >= BattleConst.FamilyFirst && weaponFamily == family);
}
