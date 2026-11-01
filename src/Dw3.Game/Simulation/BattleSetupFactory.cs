using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// The manifest-to-simulation glue for a battle: it turns the disc-decoded Conversion records
/// (encounter/fighter/tech manifests) into the engine-free BattleSetup and the flow
/// controller's technique configuration. Every number is the disc value; nothing is invented.
/// </summary>
public static class BattleSetupFactory
{
    /// <summary>PartnerEntry.skills stores SKILL_ID in bits 0..12 and flags above it
    /// (game_state.h). All text and TECHS lookups must strip those flags.</summary>
    public const int SkillIdMask = 0x1FFF;
    public static int SkillId(int skill) => skill & SkillIdMask;
    /// <summary>Maps a Conversion.TechData record to the Simulation.TechData the damage math reads.</summary>
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
    public static BattleStats ToPlayerStats(Conversion.DigimonData digimon, int level)
    {
        var stats = new BattleStats { Level = level, Family = digimon.Family };
        for (var i = 0; i < 5; i++) stats.Stats[i] = digimon.BattleStats[i];
        for (var i = 0; i < 7; i++) stats.Resist[i] = digimon.Resistances[i];
        for (var i = 0; i < 5; i++) stats.Resist[7 + i] = digimon.StatusResists[i];
        return stats;
    }

    /// <summary>
    /// A partner's battle stats at a level, base plus per-level growth. The source grows each
    /// battle stat by the Digimon's statGrowth entry (one increment per stat column) with a
    /// random swing; the slice's deterministic stand-in is base + growth x (level - 1), the
    /// five fight stats (strength/defense/spirit/wisdom/speed — charisma is dropped like
    /// ToPlayerStats). Level 1 is exactly the base.
    /// </summary>
    public static BattleStats ToPlayerStatsAtLevel(Conversion.DigimonData digimon, int level)
    {
        var stats = ToPlayerStats(digimon, level);
        var grown = level - 1;
        if (grown <= 0) return stats;
        for (var i = 0; i < 5 && i < digimon.StatGrowth.Count; i++)
            stats.Stats[i] += digimon.StatGrowth[i] * grown;
        return stats;
    }

    /// <summary>The resolved basic attack (the partner's first always-known skill, source
    /// WFIGHTMN_runCommand case 0 -> FIGHTSTG_startFirstTech -> skills[0]).</summary>
    public static TechData PlayerBasicTech(FighterData fighters, TechniqueData techs, int digimonId)
    {
        if (!fighters.Digimon.TryGetValue(digimonId, out var digimon) || digimon.Skills.Count == 0)
            return new TechData();
        var skill = digimon.Skills[0];
        return skill != 0 && techs.Techs.TryGetValue(skill, out var tech) ? ToTech(tech) : new TechData();
    }

    /// <summary>An enemy's raw battle-table entry, as DamageCalculator.ComputeEnemyStats consumes it.</summary>
    public static BattleStats ToEnemyBaseStats(Conversion.BattleTableEntry entry)
    {
        var stats = new BattleStats { Family = entry.Family };
        for (var i = 0; i < 5; i++) stats.Stats[i] = entry.Stats[i];
        for (var i = 0; i < 12; i++) stats.Resist[i] = entry.Resist[i];
        return stats;
    }

    /// <summary>Builds the party for the slice: one partner per non-zero entry of
    /// <paramref name="digimonIds"/>, resolved from DIGIMON_DATA at the given level. Stats,
    /// HP and MP grow by base + growth x (level - 1) (STFGTREP_raiseStats' deterministic
    /// stand-in); level 1 keeps the exact base values.</summary>
    public static PartnerSeed?[] BuildParty(FighterData fighters, int level, IReadOnlyList<int> digimonIds)
    {
        var partners = new PartnerSeed?[3];
        for (var i = 0; i < 3 && i < digimonIds.Count; i++)
        {
            var id = digimonIds[i];
            if (id == 0 || !fighters.Digimon.TryGetValue(id, out var digimon)) continue;
            var grown = level - 1;
            var hp = digimon.Hp + digimon.HpGrowth * grown;
            var mp = digimon.Mp + digimon.MpGrowth * grown;
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

    /// <summary>Maps a Conversion.BattleAction (disc-decoded) to the Simulation BattleTableAction
    /// the enemy AI reads.</summary>
    public static BattleTableAction ToAction(Conversion.BattleAction action) =>
        new(action.Target, action.Condition, action.ConditionArg);

    /// <summary>Builds the enemies of one encounter, resolving each enemy pool index to its fighter,
    /// level, HP/MP, strength and the battle-table entry that supplies its base stats and AI table.</summary>
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
            if (poolIndex == 0 || !byIndex.TryGetValue(poolIndex, out var source)) continue;
            if (!table.TryGetValue(source.Fighter, out var battleEntry))
                throw new InvalidOperationException($"fighter {source.Fighter} has no battle-table entry");
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
            };
        }
        return enemies;
    }

    /// <summary>The active partner's techniques (its non-zero learned skills), for the submenu.</summary>
    public static IReadOnlyList<BattleTechnique> PlayerTechniques(FighterData fighters, TechniqueData techs, int digimonId, BattleTextCatalog text)
    {
        if (!fighters.Digimon.TryGetValue(digimonId, out var digimon)) return [];
        var result = new List<BattleTechnique>();
        // DIGIMON_DATA.skills[0] is the basic attack (FIGHTSTG_startFirstTech), not a
        // Technique-menu entry. The slice has no PartnerEntry save slots yet, so entries
        // 1..6 provide its recovered menu pool (the test team uses its signature at slot 6).
        foreach (var rawSkill in digimon.Skills.Skip(1))
        {
            var skill = SkillId(rawSkill);
            if (skill == 0 || !techs.Techs.TryGetValue(skill, out var tech)) continue;
            var name = text.Technique(skill);
            result.Add(new BattleTechnique(skill, name.Debug, ToTech(tech), name));
        }
        return result;
    }

    /// <summary>Assembles the complete battle flow for a slice encounter: the party (the given
    /// partner digimon ids), the encounter's enemies, the active fighters' basic attacks and
    /// technique menus, and the display labels.</summary>
    public static BattleFlowController BuildFlow(
        FighterData fighters, TechniqueData techs, EncounterData encounters, int encounterIndex,
        IReadOnlyList<int> partyDigimonIds, IReadOnlyDictionary<int, int> nameIds, BattleTextCatalog text, int level)
    {
        _ = level;
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
            enemyBasic = ToTech(enemySkill);

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

    /// <summary>Maps a Conversion.BattleItemData (LoadBattleItems) to the Simulation
    /// BattleItem the core's item command consumes.</summary>
    public static BattleItem ToItem(Conversion.BattleItemData item) =>
        new(item.Id, item.Effect, item.Target, item.Amount);

    /// <summary>
    /// The Digimon forms the Digivolution command's menu offers a partner, from its evolution
    /// tree (STFGTREP_evolutionLists, LoadDigivolutions). The tree is the slice's stand-in for
    /// the partner's owned forms from the save system (out of scope for the slice):
    /// system tracks them yet: only the level-gated entries (stat 7, value &lt;= level) are kept,
    /// and the digimon-prerequisite needs are not checked — both named simplifications.
    /// <paramref name="partnerIndex"/> is the 0-based STFGTREP partner index (not a party slot).
    /// </summary>
    public static IReadOnlyList<int> DigivolutionForms(DigivolutionData digivolutions, int partnerIndex, int level)
    {
        var partner = digivolutions.Partners.FirstOrDefault(candidate => candidate.Partner == partnerIndex);
        if (partner is null) return [];
        return [.. partner.Evolutions
            .Where(entry => entry.Stat == 7 && entry.Value <= level && entry.DigimonId != 0)
            .Select(entry => entry.DigimonId)];
    }

    /// <summary>
    /// STARTER_PARTIES[STARTER_PACK_BALANCED] = { PARTNER_KOTEMON=0, PARTNER_RENAMON=6,
    /// PARTNER_PATAMON=7 }. The first 8 DIGIMON_DATA entries are the partners:
    /// so partner i is DIGIMON_DATA[i] and its Digimon is DIGIMON_DATA[i].id. The ids are read
    /// from the ordered manifest list, never hard-coded.
    /// </summary>
    public static IReadOnlyList<int> StarterPartnerDigimonIds(FighterData fighters)
    {
        int[] balancedPack = [0, 6, 7];
        var result = new List<int>();
        foreach (var partner in balancedPack)
            if (partner >= 0 && partner < fighters.DigimonOrdered.Count)
                result.Add(fighters.DigimonOrdered[partner].Id);
        return result;
    }

    /// <summary>The DIGIMON_DATA array index whose id is <paramref name="digimonId"/> (source
    /// findDigimon, the first 52 entries searched), or -1.</summary>
    public static int DigimonIndexFor(FighterData fighters, int digimonId)
    {
        for (var i = 0; i < fighters.DigimonOrdered.Count; i++)
            if (fighters.DigimonOrdered[i].Id == digimonId) return i;
        return -1;
    }
}
