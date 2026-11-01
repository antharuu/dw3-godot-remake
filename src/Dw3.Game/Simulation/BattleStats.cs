namespace Dw3Production.Simulation;

/// <summary>
/// A side's battle stats as FIGHTSTG_computeStats resolves them (battle.h BattleStats).
/// For an enemy, DamageCalculator.ComputeEnemyStats fills it from the raw battle-table
/// entry scaled by strength/16. For a partner it is injected resolved (synthetic).
/// </summary>
public sealed class BattleStats
{
    public int Level { get; set; }

    public int[] Stats { get; set; } = new int[5]; // attack, defense, spirit, wisdom, speed

    public int[] Resist { get; set; } = new int[12]; // 0..6 elemental, 7..11 status

    public int Flags { get; set; }

    public int Family { get; set; }

    public int DamageBonus { get; set; } // the fighter's charge, in 64ths

    public int DamageCut { get; set; } // taken off each hit

    public int[] WeaponFamilies { get; set; } = new int[3];

    public int Element { get; set; } // the accessory's element

    public int ElementPower { get; set; }

    public int TripleHit { get; set; }

    public int CriticalBonus { get; set; }

    public int Counter { get; set; }

    public int Accuracy { get; set; }

    public int Evasion { get; set; }

    public int RunAwayBonus { get; set; }

    public int RunAwayGuard { get; set; }

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
