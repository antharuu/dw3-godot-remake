namespace Dw3Production.Simulation;

/// <summary>
/// One of the battle's fighters, three per side, of which one per side is active. Engine-free.
/// A partner's HP/MP go back to the party when the battle ends.
/// </summary>
/// <remarks>
/// The resolved battle stats (<see cref="Stats"/>) and the enemy's <see cref="Strength"/> are
/// computed once when the fighter is created: the original game derives them on every use from
/// the fighter table (enemy) or from partner totals and equipment (player). Resolving them up
/// front keeps this class engine-free and independent of the extracted tables during the battle.
/// </remarks>
public sealed class BattleFighter
{
    private BattleStats _baseStats = new();
    private PartnerSeed? _partner;

    /// <summary>Gets or sets the Digimon id the fighter currently is, which selects its model, name and techniques. It changes during a digivolution.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the fighter's own Digimon id, kept while <see cref="Id"/> holds a temporary form so the form can be undone.</summary>
    public int PrevId { get; set; }

    /// <summary>Gets or sets the number of turns an enemy has played, read by the every-N-turns condition of its action table.</summary>
    public int Turns { get; set; }

    /// <summary>Gets or sets the maximum HP, the reference for HP threshold conditions and healing caps.</summary>
    public int MaxHp { get; set; }

    /// <summary>Gets or sets the current HP. 0 or less means knocked out.</summary>
    public int Hp { get; set; }

    /// <summary>Gets or sets the maximum MP.</summary>
    public int MaxMp { get; set; }

    /// <summary>Gets or sets the current MP, spent by techniques.</summary>
    public int Mp { get; set; }

    /// <summary>Gets or sets the charge added to the next technique's damage, in 64ths of the damage. 0 means no charge.</summary>
    public int Charge { get; set; }

    /// <summary>Gets or sets the four stat boost levels of the fighter. A negative value is a lowered stat, which enemy conditions 15..17 test on entries 0..2.</summary>
    public int[] Boosts { get; set; } = new int[4];

    /// <summary>Gets or sets the item id attached to the fighter, 0 for none.</summary>
    public int Item { get; set; }

    /// <summary>Gets or sets a value indicating whether the fighter is in a temporary form. Its techniques then cost no MP and their menu names come from the temporary form.</summary>
    public bool Temporary { get; set; }

    /// <summary>Gets or sets a value indicating whether the fighter is a special one, which doubles its critical hit bonus.</summary>
    public bool Special { get; set; }

    /// <summary>Gets or sets the status bits, a combination of <see cref="FighterFlags"/> values.</summary>
    public int Flags { get; set; }

    /// <summary>Gets or sets the paralysis counter, 0 when not paralyzed. An eighth of it is added to the critical chance of hits against the fighter.</summary>
    public int Paralysis { get; set; }

    /// <summary>Gets or sets the sleep counter, 0 when awake. An eighth of it is added to the critical chance of hits against the fighter.</summary>
    public int Sleep { get; set; }

    /// <summary>Gets or sets the confusion counter, 0 when not confused. Half of it is added to the critical chance of hits against the fighter.</summary>
    public int Confusion { get; set; }

    /// <summary>Gets or sets the currently resolved battle stats.</summary>
    public BattleStats Stats { get; set; } = new();

    /// <summary>Gets or sets the enemy's strength multiplier in sixteenths (16 = ×1), 0 for a partner.</summary>
    public int Strength { get; set; }

    /// <summary>Gets or sets the enemy's three technique ids from its fighter table entry. Unused for a partner.</summary>
    public int[] Techs { get; set; } = new int[3];

    /// <summary>Gets or sets its four actions (0..2 conditional, 3 the untested fallback).</summary>
    public BattleTableAction[] Actions { get; set; } = [];

    /// <summary>Gets or sets its counter-attack entry from the fighter table (condition 0 means no counter).</summary>
    public BattleTableAction Counter { get; set; }

    /// <summary>Gets a value indicating whether HP reached 0. A knocked-out fighter cannot act or be switched in.</summary>
    public bool IsKnockedOut => Hp <= 0;

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
            _partner = seed,
            _baseStats = seed.Stats,
            Stats = seed.Stats,
        };
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
        fighter._baseStats = fighter.Stats;
        fighter.RefreshStats();
        return fighter;
    }

    /// <summary>Refreshes the stat snapshot used by the next rule calculation from the current form and mutable battle modifiers.</summary>
    public void RefreshStats()
    {
        BattleStats source = _partner is null ? Stats : _baseStats;
        Stats = BattleStatResolver.Resolve(source, _partner, Id, Flags, Charge, Boosts);
    }

    /// <summary>Takes <paramref name="damage"/> off the fighter's HP, which never drops below 0.</summary>
    /// <param name="damage">The hit's damage, as the damage formulas return it.</param>
    public void TakeDamage(int damage)
    {
        Hp = Math.Max(Hp - damage, 0);
    }
}
