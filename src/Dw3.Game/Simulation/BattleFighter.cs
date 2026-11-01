namespace Dw3Production.Simulation;

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
    public int Id { get; set; } // the Digimon (DIGIMON_DATA)

    public int PrevId { get; set; } // its own id while id is a temporary one

    public int Turns { get; set; } // an enemy's, for its battle table's conditions

    public int MaxHp { get; set; }

    public int Hp { get; set; }

    public int MaxMp { get; set; }

    public int Mp { get; set; }

    public int Charge { get; set; } // added in 64ths to its next technique's damage

    public int[] Boosts { get; set; } = new int[4];

    public int Item { get; set; }

    public bool Temporary { get; set; }

    public bool Special { get; set; }

    public int Flags { get; set; } // FighterFlags

    public int Paralysis { get; set; }

    public int Sleep { get; set; }

    public int Confusion { get; set; }

    /// <summary>Gets or sets the resolved battle stats (see class comment).</summary>
    public BattleStats Stats { get; set; } = new();

    /// <summary>Gets or sets the enemy's strength in 16ths (BattleEnemy.strength); 0 for a partner.</summary>
    public int Strength { get; set; }

    /// <summary>Gets or sets the enemy's battle-table techniques techs[0..2] (battle.h BattleTableEntry).</summary>
    public int[] Techs { get; set; } = new int[3];

    /// <summary>Gets or sets its four actions (0..2 conditional, 3 the untested fallback).</summary>
    public BattleTableAction[] Actions { get; set; } = [];

    /// <summary>Gets or sets its counter-attack entry from the battle table (condition 0 means no counter).</summary>
    public BattleTableAction Counter { get; set; }

    public bool IsKnockedOut => Hp <= 0;

    /// <summary>Takes <paramref name="damage"/> off the fighter's HP, which never drops below 0.</summary>
    /// <param name="damage">The hit's damage, as the damage formulas return it.</param>
    public void TakeDamage(int damage)
    {
        Hp = Math.Max(Hp - damage, 0);
    }

    /// <summary>
    /// Loads a party member for the battle. The fighter shares the seed's resolved stats object,
    /// whose flags and damage bonus are reset to the fresh fighter's (no status, no charge).
    /// </summary>
    /// <param name="seed">The party slot's HP, MP and resolved stats.</param>
    /// <param name="id">The Digimon the partner fights as (the active Digimon for slot 0).</param>
    /// <returns>The loaded fighter.</returns>
    public static BattleFighter FromPartner(PartnerSeed seed, int id)
    {
        var fighter = new BattleFighter
        {
            Id = id,
            Hp = seed.Hp,
            MaxHp = seed.MaxHp,
            Mp = seed.Mp,
            MaxMp = seed.MaxMp,
            Stats = seed.Stats,
        };
        fighter.Stats.Flags = fighter.Flags;
        fighter.Stats.DamageBonus = fighter.Charge;
        return fighter;
    }

    /// <summary>
    /// Loads an enemy for the battle, at full HP and MP, with its battle-table stats scaled by
    /// its strength in 16ths and its action table and counter copied over.
    /// </summary>
    /// <param name="enemy">The encounter's enemy entry.</param>
    /// <returns>The loaded fighter.</returns>
    public static BattleFighter FromEnemy(BattleEnemy enemy)
    {
        var fighter = new BattleFighter
        {
            Id = enemy.Fighter,
            Hp = enemy.Hp,
            MaxHp = enemy.Hp,
            Mp = enemy.Mp,
            MaxMp = enemy.Mp,
            Strength = enemy.Strength,
            Techs = enemy.Techs,
            Actions = enemy.Actions,
            Counter = enemy.Counter,
        };
        fighter.Stats = DamageCalculator.ComputeEnemyStats(enemy.BaseStats, enemy.Level, enemy.Strength);
        fighter.Stats.Flags = fighter.Flags;
        fighter.Stats.DamageBonus = fighter.Charge;
        return fighter;
    }
}
