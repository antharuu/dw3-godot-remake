using System.Text.Json;

namespace Dw3.Formats.Battle;

/// <summary>Writes deterministic, source-shaped JSON projections of decoded battle facts.</summary>
public static class BattleJsonWriter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string WriteBattle(
        IReadOnlyList<DigimonRecord> digimon,
        IReadOnlyList<TechniqueRecord> techniques,
        IReadOnlyList<BattleTableRecord> enemyTable,
        IReadOnlyList<(ItemRecord Item, ItemEffect? Effect)> items,
        IReadOnlyList<BattleReward> rewards,
        BattleGrowthData growth) => JsonSerializer.Serialize(new
        {
            schema = "m9b-battle-v2",
            digimon = digimon.Select(ProjectDigimon),
            techniques = techniques.Select(ProjectTechnique),
            enemyTable = enemyTable.Select(ProjectEnemy),
            items = items.Select(ProjectItem),
            rewards = rewards.Select((value, index) => new { id = index, value.DigimonExperience, value.Experience, value.Money }),
            growth = new { growth.LevelExperience, growth.VitalCuts, growth.VitalRandom, growth.StatGains, growth.ResistanceGains },
        }, Options);

    public static string WriteDigivolution(IReadOnlyList<DigimonRecord> digimon, IReadOnlyList<IReadOnlyList<EvolutionRecord>> partners) => JsonSerializer.Serialize(new
    {
        schema = "m9b-digivolution-v2",
        partners = partners.Select((records, partner) => new
        {
            partner,
            baseId = digimon[partner].Id,
            evolutions = records.Select((value, ordinal) => new
            {
                ordinal,
                value.DigimonIndex,
                value.DigimonId,
                needs = value.Needs.Select(need => new { need.DigimonIndex, need.DigimonId, need.Level }),
                value.Stat,
                value.Value,
            }),
        }),
    }, Options);

    /// <summary>Projects one creature record; <c>index</c> is its one-based position in the creature table.</summary>
    /// <param name="value">Creature record.</param>
    /// <param name="index">Zero-based table position.</param>
    /// <returns>The serialisable projection.</returns>
    private static object ProjectDigimon(DigimonRecord value, int index) => new
    {
        index = index + 1,
        value.Id,
        value.BattleStats,
        value.Resistances,
        value.Skills,
        value.PairTechnique,
        value.StatusResistances,
        value.SkillLevels,
        value.KnownLevels,
        value.ExperienceLevel,
        value.PairPartner,
        value.ExperienceRate,
        value.Hp,
        value.Mp,
        value.HpGrowth,
        value.MpGrowth,
        value.StatGrowth,
        value.ResistGrowth,
        value.BlastForms,
        value.NameId,
        value.Family,
    };

    /// <summary>Projects one technique record; <c>id</c> is its one-based position in the technique table.</summary>
    /// <param name="value">Technique record.</param>
    /// <param name="index">Zero-based table position.</param>
    /// <returns>The serialisable projection.</returns>
    private static object ProjectTechnique(TechniqueRecord value, int index) => new
    {
        id = index + 1,
        value.Mp,
        value.Power,
        value.Icon,
        value.Kind,
        value.Accuracy,
        value.Element,
        value.ElementPower,
        value.Family,
        value.Effect,
        value.EffectChance,
        value.EffectPower,
        value.ScriptStage,
        value.ScriptEffect,
        value.ScriptSound,
        value.Script,
        value.HitCount,
    };

    /// <summary>Projects one enemy battle configuration with its four actions and counter action.</summary>
    /// <param name="value">Enemy table record.</param>
    /// <returns>The serialisable projection.</returns>
    private static object ProjectEnemy(BattleTableRecord value) => new
    {
        value.Id,
        value.Item,
        value.ItemChance,
        value.NameId,
        value.Techniques,
        value.Stats,
        value.Resistances,
        value.Family,
        actions = value.Actions.Select(ProjectAction),
        counter = ProjectAction(value.Counter),
    };

    /// <summary>Projects one item record and its optional effect; <c>id</c> is its one-based position.</summary>
    /// <param name="value">Item record and the effect its data pointer addresses, if any.</param>
    /// <param name="index">Zero-based table position.</param>
    /// <returns>The serialisable projection.</returns>
    private static object ProjectItem((ItemRecord Item, ItemEffect? Effect) value, int index) => new
    {
        id = index + 1,
        dataPointer = value.Item.DataPointer,
        value.Item.Price,
        value.Item.SellPrice,
        value.Item.Kind,
        value.Item.Type,
        effect = value.Effect is null ? null : new { value.Effect.Flags, value.Effect.Kind, value.Effect.Amount },
    };

    private static object ProjectAction(BattleAction value) => new { value.Target, value.Condition, conditionArgument = value.ConditionArgument };
}
