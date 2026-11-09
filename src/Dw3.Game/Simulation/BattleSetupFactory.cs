using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// The manifest-to-simulation glue for a battle: it turns the disc-decoded Conversion records
/// (encounter/fighter/tech manifests) into the engine-free <see cref="BattleSetup"/> and the flow
/// controller's technique configuration. Every number is the disc value; nothing is invented.
/// </summary>
public static class BattleSetupFactory
{
    /// <summary>Mask of the technique id inside a partner skill word: bits 0..12 hold the id and the
    /// bits above hold learning flags, so every text and technique lookup must apply it first.</summary>
    public const int SkillIdMask = 0x1FFF;

    /// <summary>Copies every encounter-owned rule field into a battle setup before the shared state is created.</summary>
    /// <param name="entry">Decoded encounter that supplies opening and restriction values.</param>
    /// <param name="setup">Prepared setup receiving the bounded encounter values.</param>
    public static void ApplyEncounterSettings(EncounterEntry entry, BattleSetup setup)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(setup);
        setup.AmbushChance = entry.AmbushChance;
        setup.EncounterKind = entry.Kind;
        Array.Clear(setup.Blocks);
        for (var index = 0; index < Math.Min(entry.Blocks.Count, setup.Blocks.Length); index++)
        {
            setup.Blocks[index] = entry.Blocks[index];
        }
    }

    /// <summary>Strips the learning flags from a partner skill word.</summary>
    /// <param name="skill">Skill word as stored in the Digimon table.</param>
    /// <returns>The technique id, 0..0x1FFF. 0 means an empty skill slot.</returns>
    public static int SkillId(int skill) => skill & SkillIdMask;

    /// <summary>Copies a decoded technique record field by field into the simulation's technique, so the damage math never depends on the extraction types.</summary>
    /// <param name="tech">Technique decoded from the technique table.</param>
    /// <returns>A new simulation technique with the same values.</returns>
    public static TechData ToTech(Conversion.TechData tech) => new()
    {
        Mp = tech.Mp,
        Power = tech.Power,
        Icon = tech.Icon,
        Kind = tech.Kind,
        Accuracy = tech.Accuracy,
        Element = tech.Element,
        ElementPower = tech.ElementPower,
        Family = tech.Family,
        Effect = tech.Effect,
        EffectChance = tech.EffectChance,
        EffectPower = tech.EffectPower,
        ScriptStage = tech.ScriptStage,
        ScriptEffect = tech.ScriptEffect,
        ScriptSound = tech.ScriptSound,
        Script = tech.Script,
        HitCount = tech.HitCount,
    };

    /// <summary>A partner's resolved battle stats: the five battleStats columns, the seven elemental
    /// resistances plus the five status resistances, and its family. Level is the slice's start level.</summary>
    /// <param name="digimon">The partner's entry of the Digimon table.</param>
    /// <param name="level">Level stored in the stats. Stats are not grown here, see <see cref="ToPlayerStatsAtLevel"/>.</param>
    /// <returns>Stats with the base values of the table entry.</returns>
    public static BattleStats ToPlayerStats(Conversion.DigimonData digimon, int level)
    {
        var stats = new BattleStats { Level = level, Family = digimon.Family };
        for (var i = 0; i < 5; i++)
        {
            stats.Stats[i] = digimon.BattleStats[i];
        }

        for (var i = 0; i < 7; i++)
        {
            stats.Resist[i] = digimon.Resistances[i];
        }

        for (var i = 0; i < 5; i++)
        {
            stats.Resist[7 + i] = digimon.StatusResists[i];
        }

        return stats;
    }

    /// <summary>
    /// A partner's battle stats at a level, base plus per-level growth. The original game grows each
    /// battle stat by the Digimon's stat growth entry (one increment per stat column) with a
    /// random swing; this deterministic stand-in is base + growth × (level - 1) on the
    /// five fight stats (attack, defense, spirit, wisdom, speed; charisma is dropped as in
    /// <see cref="ToPlayerStats"/>). Level 1 is exactly the base.
    /// </summary>
    /// <param name="digimon">The partner's entry of the Digimon table.</param>
    /// <param name="level">Target level, 1 or more. Lower values return the base stats.</param>
    /// <returns>Stats grown to <paramref name="level"/>, resistances unchanged.</returns>
    public static BattleStats ToPlayerStatsAtLevel(Conversion.DigimonData digimon, int level)
    {
        var stats = ToPlayerStats(digimon, level);
        var grown = level - 1;
        if (grown <= 0)
        {
            return stats;
        }

        for (var i = 0; i < 5 && i < digimon.StatGrowth.Count; i++)
        {
            stats.Stats[i] += digimon.StatGrowth[i] * grown;
        }

        return stats;
    }

    /// <summary>Resolves the basic attack of a partner: its first skill, which it always knows and which the Attack command uses.</summary>
    /// <param name="fighters">Fighter data holding the Digimon table.</param>
    /// <param name="techs">Technique table.</param>
    /// <param name="digimonId">Digimon id of the partner.</param>
    /// <returns>The technique, or an empty technique (all zero) when the Digimon, its first skill or the technique is missing.</returns>
    public static TechData PlayerBasicTech(FighterData fighters, TechniqueData techs, int digimonId)
    {
        if (!fighters.Digimon.TryGetValue(digimonId, out var digimon) || digimon.Skills.Count == 0)
        {
            return new TechData();
        }

        var skill = digimon.Skills[0];
        return skill != 0 && techs.Techs.TryGetValue(skill, out var tech) ? ToTech(tech) : new TechData();
    }

    /// <summary>Copies the five stats, twelve resistances and family of a fighter table entry, unscaled, as <see cref="DamageCalculator.ComputeEnemyStats"/> expects them.</summary>
    /// <param name="entry">Fighter table entry of the enemy.</param>
    /// <returns>Base stats before the strength and level scaling.</returns>
    public static BattleStats ToEnemyBaseStats(Conversion.BattleTableEntry entry)
    {
        var stats = new BattleStats { Family = entry.Family };
        for (var i = 0; i < 5; i++)
        {
            stats.Stats[i] = entry.Stats[i];
        }

        for (var i = 0; i < 12; i++)
        {
            stats.Resist[i] = entry.Resist[i];
        }

        return stats;
    }

    /// <summary>Builds the party for the slice: one partner per non-zero entry of
    /// <paramref name="digimonIds"/>, resolved from the Digimon table at the given level. Stats,
    /// HP and MP grow by base + growth × (level - 1), a deterministic stand-in for the random
    /// level-up growth; level 1 keeps the exact base values.</summary>
    /// <param name="fighters">Fighter data holding the Digimon table.</param>
    /// <param name="level">Level of every partner, 1 or more.</param>
    /// <param name="digimonIds">Up to three Digimon ids, 0 for an empty slot.</param>
    /// <returns>Three slots, at full HP and MP. A slot stays null when its id is 0 or unknown.</returns>
    public static PartnerSeed?[] BuildParty(FighterData fighters, int level, IReadOnlyList<int> digimonIds)
    {
        var partners = new PartnerSeed?[3];
        for (var i = 0; i < 3 && i < digimonIds.Count; i++)
        {
            var id = digimonIds[i];
            if (id == 0 || !fighters.Digimon.TryGetValue(id, out var digimon))
            {
                continue;
            }

            var grown = level - 1;
            var hp = digimon.Hp + (digimon.HpGrowth * grown);
            var mp = digimon.Mp + (digimon.MpGrowth * grown);
            partners[i] = new PartnerSeed
            {
                Id = id,
                Hp = hp,
                MaxHp = hp,
                Mp = mp,
                MaxMp = mp,
                Stats = ToPlayerStatsAtLevel(digimon, level),
            };
        }

        return partners;
    }

    /// <summary>Maps a decoded fighter table action to the action triple the enemy AI reads.</summary>
    /// <param name="action">Decoded action.</param>
    /// <returns>The action with the same target, condition and argument.</returns>
    public static BattleTableAction ToAction(Conversion.BattleAction action) =>
        new(action.Target, action.Condition, action.ConditionArg);

    /// <summary>Builds the enemies of one encounter, resolving each enemy pool index to its fighter,
    /// level, HP/MP, strength and the fighter table entry that supplies its base stats and AI table.</summary>
    /// <param name="encounters">Encounter data holding the encounter list and the enemy pool.</param>
    /// <param name="encounterIndex">Index of the encounter to build.</param>
    /// <param name="fighters">Fighter data holding the fighter table.</param>
    /// <returns>Three slots. A slot stays null when its pool index is 0 or absent from the pool.</returns>
    /// <exception cref="InvalidOperationException">The encounter is not in the data, or an enemy's fighter has no fighter table entry.</exception>
    public static BattleEnemy?[] BuildEnemies(EncounterData encounters, int encounterIndex, FighterData fighters)
    {
        var entry = encounters.Encounters.FirstOrDefault(candidate => candidate.Index == encounterIndex)
            ?? throw new InvalidOperationException($"encounter {encounterIndex} not in the manifest");
        var byIndex = encounters.Enemies.ToDictionary(enemy => enemy.Index);
        var table = fighters.BattleTable.ToDictionary(entry => entry.Id);
        var enemies = new BattleEnemy?[3];
        for (var i = 0; i < 3 && i < entry.EnemyIndices.Count; i++)
        {
            var poolIndex = entry.EnemyIndices[i];
            if (poolIndex == 0 || !byIndex.TryGetValue(poolIndex, out var source))
            {
                continue;
            }

            if (!table.TryGetValue(source.Fighter, out var battleEntry))
            {
                throw new InvalidOperationException($"fighter {source.Fighter} has no battle-table entry");
            }

            enemies[i] = new BattleEnemy
            {
                Fighter = source.Fighter,
                Level = source.Level,
                Hp = source.Hp,
                Mp = source.Mp,
                Strength = source.Strength,
                BaseStats = ToEnemyBaseStats(battleEntry),
                Techs = [.. battleEntry.Techs],
                Actions = [.. battleEntry.Actions.Select(ToAction)],
                Counter = ToAction(battleEntry.Counter),
                Item = battleEntry.Item,
                ItemChance = battleEntry.ItemChance,
            };
        }

        return enemies;
    }

    /// <summary>The active partner's techniques (its non-zero learned skills), for the submenu.</summary>
    /// <param name="fighters">Fighter data holding the Digimon table.</param>
    /// <param name="techs">Technique table.</param>
    /// <param name="digimonId">Digimon id of the active partner.</param>
    /// <param name="text">Battle text catalogue that names each technique.</param>
    /// <returns>The techniques in skill slot order, skipping slot 0 (the basic attack) and unknown ids. Empty for an unknown Digimon.</returns>
    public static IReadOnlyList<BattleTechnique> PlayerTechniques(FighterData fighters, TechniqueData techs, int digimonId, BattleTextCatalog text)
    {
        if (!fighters.Digimon.TryGetValue(digimonId, out var digimon))
        {
            return [];
        }

        var result = new List<BattleTechnique>();

        // Skill slot 0 is the basic attack, not a
        // Technique-menu entry. The slice has no PartnerEntry save slots yet, so entries
        // 1..6 provide its recovered menu pool (the test team uses its signature at slot 6).
        foreach (var rawSkill in digimon.Skills.Skip(1))
        {
            var skill = SkillId(rawSkill);
            if (skill == 0 || !techs.Techs.TryGetValue(skill, out var tech))
            {
                continue;
            }

            var name = text.Technique(skill);
            result.Add(new BattleTechnique(skill, name.Debug, ToTech(tech), name));
        }

        return result;
    }

    /// <summary>Assembles the complete battle flow for a slice encounter: the party (the given
    /// partner digimon ids), the encounter's enemies, the active fighters' basic attacks and
    /// technique menus, and the display labels.</summary>
    /// <param name="fighters">Fighter data holding the Digimon and fighter tables.</param>
    /// <param name="techs">Technique table.</param>
    /// <param name="encounters">Encounter data.</param>
    /// <param name="encounterIndex">Index of the encounter fought.</param>
    /// <param name="partyDigimonIds">Digimon ids of the party, slot 0 being the active partner. Must hold at least one id.</param>
    /// <param name="nameIds">Map from fighter id to name id, used for messages.</param>
    /// <param name="text">Battle text catalogue.</param>
    /// <returns>A flow controller ready for <see cref="BattleFlowController.Start"/>. A missing basic attack is an empty technique.</returns>
    /// <exception cref="InvalidOperationException">The encounter is not in the data.</exception>
    public static BattleFlowController BuildFlow(
        FighterData fighters,
        TechniqueData techs,
        EncounterData encounters,
        int encounterIndex,
        IReadOnlyList<int> partyDigimonIds,
        IReadOnlyDictionary<int, int> nameIds,
        BattleTextCatalog text)
    {
        var entry = encounters.Encounters.FirstOrDefault(candidate => candidate.Index == encounterIndex)
            ?? throw new InvalidOperationException($"encounter {encounterIndex} not in the manifest");
        var byIndex = encounters.Enemies.ToDictionary(enemy => enemy.Index);
        var table = fighters.BattleTable.ToDictionary(entry => entry.Id);

        var playerBasic = fighters.Digimon.TryGetValue(partyDigimonIds[0], out var partyDigimon)
            && techs.Techs.TryGetValue(partyDigimon.Skills[0], out var partySkill)
            ? ToTech(partySkill)
            : new TechData();

        var enemyBasic = new TechData();
        var enemyPoolIndex = entry.EnemyIndices.FirstOrDefault(index => index != 0);
        if (enemyPoolIndex != 0 && byIndex.TryGetValue(enemyPoolIndex, out var enemySource)
            && table.TryGetValue(enemySource.Fighter, out var enemyEntry)
            && enemyEntry.Techs[0] != 0 && techs.Techs.TryGetValue(enemyEntry.Techs[0], out var enemySkill))
        {
            enemyBasic = ToTech(enemySkill);
        }

        var flow = new BattleFlowController
        {
            PlayerBasicTech = playerBasic,
            EnemyBasicTech = enemyBasic,
            PlayerTechniques = PlayerTechniques(fighters, techs, partyDigimonIds[0], text),
            Techs = techs.Techs.ToDictionary(pair => pair.Key, pair => ToTech(pair.Value)),
            NameIds = nameIds,
            Text = text,
        };
        return flow;
    }

    /// <summary>Maps a decoded battle item to the item the core's item command consumes.</summary>
    /// <param name="item">Battle item from the pack.</param>
    /// <returns>The item with its id, effect, target and amount.</returns>
    public static BattleItem ToItem(Conversion.BattleItemData item) =>
        new(item.Id, item.Effect, item.Target, item.Amount);

    /// <summary>
    /// The Digimon forms the Digivolution command's menu offers a partner, from its evolution
    /// tree. The tree stands in for the forms the partner owns, which only a save system
    /// tracks: only the level-gated entries (stat 7, value &lt;= level) are kept,
    /// and the Digimon prerequisites are not checked. Both are deliberate simplifications.
    /// </summary>
    /// <param name="digivolutions">Digivolution trees of the partners.</param>
    /// <param name="partnerIndex">Zero-based partner index in the digivolution data, not a party slot.</param>
    /// <param name="level">Current level of the partner.</param>
    /// <returns>Form ids in tree order. Empty for an unknown partner.</returns>
    public static IReadOnlyList<int> DigivolutionForms(DigivolutionData digivolutions, int partnerIndex, int level)
    {
        var partner = digivolutions.Partners.FirstOrDefault(candidate => candidate.Partner == partnerIndex);
        if (partner is null)
        {
            return [];
        }

        return [.. partner.Evolutions
            .Where(entry => entry.Stat == 7 && entry.Value <= level && entry.DigimonId != 0)
            .Select(entry => entry.DigimonId)];
    }

    /// <summary>
    /// Digimon ids of the balanced starter party: partners 0 (Kotemon), 6 (Renamon) and 7 (Patamon).
    /// The first eight entries of the Digimon table are the partners, so partner i is table entry i.
    /// The ids are read from the ordered table, never hard-coded.
    /// </summary>
    /// <param name="fighters">Fighter data holding the ordered Digimon table.</param>
    /// <returns>Up to three ids, fewer when the table is shorter than eight entries.</returns>
    public static IReadOnlyList<int> StarterPartnerDigimonIds(FighterData fighters)
    {
        int[] balancedPack = [0, 6, 7];
        var result = new List<int>();
        foreach (var partner in balancedPack)
        {
            if (partner >= 0 && partner < fighters.DigimonOrdered.Count)
            {
                result.Add(fighters.DigimonOrdered[partner].Id);
            }
        }

        return result;
    }

    /// <summary>Finds the position in the ordered Digimon table of the entry with a given id.</summary>
    /// <param name="fighters">Fighter data holding the ordered Digimon table.</param>
    /// <param name="digimonId">Digimon id to find.</param>
    /// <returns>The first matching index, or -1 when no entry has that id.</returns>
    public static int DigimonIndexFor(FighterData fighters, int digimonId)
    {
        for (var i = 0; i < fighters.DigimonOrdered.Count; i++)
        {
            if (fighters.DigimonOrdered[i].Id == digimonId)
            {
                return i;
            }
        }

        return -1;
    }
}
