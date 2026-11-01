namespace Dw3Production.Simulation;

/// <summary>BATTLE_STAT_* indices into BattleStats.Stats (battle.h).</summary>
public static class BattleStat
{
    public const int Attack = 0;   // physical damage
    public const int Defense = 1;  // against it
    public const int Spirit = 2;   // magic damage, and against it
    public const int Wisdom = 3;   // magic accuracy, statuses and heals
    public const int Speed = 4;    // physical accuracy, evasion and running away
}

/// <summary>Sentinel values shared by elements, families, techniques and effects.</summary>
public static class BattleConst
{
    public const int ElementFirst = 2;
    public const int ElementCount = 7;
    public const int FamilyFirst = 2;
    public const int TechPhysical = 2;       // TechData.icon
    public const int TechMagic = 3;          // TechData.icon
    public const int TechEffectFirst = 2;
    public const int TechEffectCritical = 11;
    public const int BlockRunAway = 11;      // BattleSetup.blocks index
}

/// <summary>RESIST_* indices (battle.h): 0..6 elemental, 7..11 status.</summary>
public static class Resist
{
    public const int Poison = 7;
    public const int Paralysis = 8;
    public const int Confusion = 9;
    public const int Sleep = 10;
    public const int KnockOut = 11;
    public const int Count = 12;
}

/// <summary>
/// A side's battle stats as FIGHTSTG_computeStats resolves them (battle.h BattleStats).
/// For an enemy, DamageCalculator.ComputeEnemyStats fills it from the raw battle-table
/// entry scaled by strength/16. For a partner it is injected resolved (synthetic).
/// </summary>
public sealed class BattleStats
{
    public int Level;
    public int[] Stats = new int[5];       // attack, defense, spirit, wisdom, speed
    public int[] Resist = new int[12];     // 0..6 elemental, 7..11 status
    public int Flags;
    public int Family;
    public int DamageBonus;                // the fighter's charge, in 64ths
    public int DamageCut;                  // taken off each hit
    public int[] WeaponFamilies = new int[3];
    public int Element;                    // the accessory's element
    public int ElementPower;
    public int TripleHit;
    public int CriticalBonus;
    public int Counter;
    public int Accuracy;
    public int Evasion;
    public int RunAwayBonus;
    public int RunAwayGuard;
    public int StealBonus;
}
