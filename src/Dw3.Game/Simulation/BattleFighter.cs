namespace Dw3Production.Simulation;

/// <summary>FIGHTER_* flags (battle.h): the statuses a technique can inflict.</summary>
public static class FighterFlags
{
    public const int Poisoned = 1;
    public const int Paralyzed = 2;
    public const int Confused = 4;
    public const int Asleep = 8;
    public const int NoSwitch = 0x10;
    public const int NoDigivolve = 0x20;
}

/// <summary>
/// One of the battle's fighters (battle.h BattleFighter), three per side. Engine-free.
/// A partner's HP/MP go back to the party when the battle ends. The resolved battle
/// stats (Stats) and the enemy's strength are adaptations: the source resolves stats
/// on demand from the battle table (enemy) or partner totals + equipment (player), and
/// reads strength from BATTLE_SETUP.enemies[i].strength; the core resolves both once at
/// InitFighters so it stays engine-free and independent of the proprietary tables.
/// </summary>
public sealed class BattleFighter
{
    public int Id;          // the Digimon (DIGIMON_DATA)
    public int PrevId;      // its own id while id is a temporary one
    public int Turns;       // an enemy's, for its battle table's conditions
    public int MaxHp;
    public int Hp;
    public int MaxMp;
    public int Mp;
    public int Charge;      // added in 64ths to its next technique's damage
    public int[] Boosts = new int[4];
    public int Item;
    public bool Temporary;
    public bool Special;
    public int Flags;       // FighterFlags
    public int Paralysis;
    public int Sleep;
    public int Confusion;

    /// <summary>The resolved battle stats (see class comment).</summary>
    public BattleStats Stats = new();

    /// <summary>The enemy's strength in 16ths (BattleEnemy.strength); 0 for a partner.</summary>
    public int Strength;

    /// <summary>The enemy's battle-table techniques techs[0..2] (battle.h BattleTableEntry).</summary>
    public int[] Techs = new int[3];

    /// <summary>Its four actions (0..2 conditional, 3 the untested fallback).</summary>
    public BattleTableAction[] Actions = [];

    /// <summary>Its counter-attack entry from the battle table (condition 0 means no counter).</summary>
    public BattleTableAction Counter;

    public bool IsKnockedOut => Hp <= 0;
}
