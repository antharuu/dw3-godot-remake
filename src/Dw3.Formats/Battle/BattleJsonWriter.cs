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
            digimon = digimon.Select((value, index) => new
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
            }),
            techniques = techniques.Select((value, index) => new
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
            }),
            enemyTable = enemyTable.Select(value => new
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
            }),
            items = items.Select((value, index) => new
            {
                id = index + 1,
                dataPointer = value.Item.DataPointer,
                value.Item.Price,
                value.Item.SellPrice,
                value.Item.Kind,
                value.Item.Type,
                effect = value.Effect is null ? null : new { value.Effect.Flags, value.Effect.Kind, value.Effect.Amount },
            }),
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

    private static object ProjectAction(BattleAction value) => new { value.Target, value.Condition, conditionArgument = value.ConditionArgument };
}
