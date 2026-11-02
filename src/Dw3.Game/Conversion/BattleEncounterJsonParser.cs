using System.Text.Json;

namespace Dw3Production.Conversion;

/// <summary>Static JSON parsing helpers for the encounter, fighter, digivolution and reward domains
/// read from battle.json and encounter.json. These helpers are pure functions: they take a
/// <see cref="JsonElement"/> and return domain records, with no disc I/O of their own.</summary>
internal static class BattleEncounterJsonParser
{
    /// <summary>Parses the validated encounter manifest root element into an <see cref="EncounterData"/>.</summary>
    /// <param name="root">The root <see cref="JsonElement"/> of the encounter manifest.</param>
    /// <returns>The fully parsed <see cref="EncounterData"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">A required field is absent or has an unexpected value.</exception>
    internal static EncounterData ParseEncounters(JsonElement root)
    {
        int[] battleRates = Ints(root.GetProperty("battleRates"));
        var enemies = ParseEnemies(root.GetProperty("enemies"));
        var encounters = ParseEncounterList(root.GetProperty("encounters"));
        var stages = ParseEncounterStages(root.GetProperty("stages"));
        if (enemies.Length == 0 || encounters.Length == 0 || stages.Count == 0)
        {
            throw new JsonException("encounter manifest is empty");
        }

        return new EncounterData(battleRates, enemies, encounters, stages);
    }

    /// <summary>Parses the fighter manifest root element into a <see cref="FighterData"/>.</summary>
    /// <param name="root">The root <see cref="JsonElement"/> of the battle.json fighter section.</param>
    /// <returns>The fully parsed <see cref="FighterData"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">The fighter inventory count is wrong.</exception>
    internal static FighterData ParseFighters(JsonElement root)
    {
        var ordered = new List<DigimonData>();
        var digimon = new Dictionary<int, DigimonData>();
        foreach (JsonElement entry in root.GetProperty("digimon").EnumerateArray())
        {
            var value = ParseDigimon(entry);
            digimon.Add(value.Id, value);
            ordered.Add(value);
        }

        var table = new List<BattleTableEntry>();
        foreach (JsonElement entry in root.GetProperty("enemyTable").EnumerateArray())
        {
            table.Add(ParseTableEntry(entry));
        }

        if (digimon.Count != 55 || table.Count == 0)
        {
            throw new JsonException("fighter inventory is incomplete");
        }

        return new FighterData(digimon, ordered, table);
    }

    /// <summary>Parses the digivolution manifest root element into a <see cref="DigivolutionData"/>.</summary>
    /// <param name="root">The root <see cref="JsonElement"/> of the digivolution manifest.</param>
    /// <returns>The fully parsed <see cref="DigivolutionData"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">The partner or evolution count is wrong.</exception>
    internal static DigivolutionData ParseDigivolutions(JsonElement root)
    {
        var partners = new List<PartnerDigivolution>();
        foreach (JsonElement partner in root.GetProperty("partners").EnumerateArray())
        {
            var evolutions = partner.GetProperty("evolutions").EnumerateArray().Select(ParseEvolution).ToArray();
            if (evolutions.Length != 44)
            {
                throw new JsonException("digivolution list must contain 44 entries");
            }

            partners.Add(new PartnerDigivolution(partner.GetProperty("partner").GetInt32(), partner.GetProperty("baseId").GetInt32(), evolutions));
        }

        if (partners.Count != 8)
        {
            throw new JsonException("digivolution data must contain eight partners");
        }

        return new DigivolutionData(partners, new DigivolutionBlast([4, 19, 39, 70]));
    }

    /// <summary>Parses the rewards section of the battle manifest root element.</summary>
    /// <param name="root">The root <see cref="JsonElement"/> of the battle.json manifest.</param>
    /// <returns>The fully parsed <see cref="RewardsData"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">The reward count is wrong.</exception>
    internal static RewardsData ParseRewards(JsonElement root)
    {
        var rewards = root.GetProperty("rewards").EnumerateArray().Select(e => new RewardEntry(
            e.GetProperty("id").GetInt32(),
            e.GetProperty("digimonExperience").GetInt32(),
            e.GetProperty("experience").GetInt32(),
            e.GetProperty("money").GetInt32())).ToArray();
        JsonElement growth = root.GetProperty("growth");
        var tables = new GrowthTablesData(
            Ints(growth.GetProperty("levelExperience")),
            Ints(growth.GetProperty("vitalCuts")),
            Ints(growth.GetProperty("vitalRandom")),
            [.. growth.GetProperty("statGains").EnumerateArray().Select(Ints).Cast<IReadOnlyList<int>>()],
            Ints(growth.GetProperty("resistanceGains")));
        if (rewards.Length != 335)
        {
            throw new JsonException($"reward inventory has {rewards.Length} entries");
        }

        return new RewardsData(rewards, tables);
    }

    private static BattleEnemy[] ParseEnemies(JsonElement element) =>
        [.. element.EnumerateArray().Select(e => new BattleEnemy(
            e.GetProperty("index").GetInt32(), e.GetProperty("fighter").GetInt32(),
            e.GetProperty("level").GetInt32(), e.GetProperty("hp").GetInt32(),
            e.GetProperty("mp").GetInt32(), e.GetProperty("strength").GetInt32()))];

    private static EncounterEntry[] ParseEncounterList(JsonElement element) =>
        [.. element.EnumerateArray().Select(e => new EncounterEntry(
            e.GetProperty("index").GetInt32(), Ints(e.GetProperty("enemyIndices")),
            [.. e.GetProperty("enemies").EnumerateArray().Select(en => en.GetProperty("fighter").GetInt32())],
            e.GetProperty("ambushChance").GetInt32(), e.GetProperty("kind").GetInt32(), Ints(e.GetProperty("blocks"))))];

    private static Dictionary<string, EncounterStage> ParseEncounterStages(JsonElement stagesElement)
    {
        var stages = new Dictionary<string, EncounterStage>();
        foreach (JsonProperty stageProperty in stagesElement.EnumerateObject())
        {
            stages.Add(stageProperty.Name, ParseOneEncounterStage(stageProperty.Value));
        }

        return stages;
    }

    private static EncounterStage ParseOneEncounterStage(JsonElement stage)
    {
        string overlay = stage.GetProperty("overlay").GetString() ?? throw new JsonException("stage overlay missing");
        string hash = stage.GetProperty("overlay_sha256").GetString() ?? throw new JsonException("stage hash missing");
        var arrays = ParseStageArrays(stage.GetProperty("stageBattles"));
        return new EncounterStage(overlay, hash, arrays);
    }

    private static List<EncounterStageArray> ParseStageArrays(JsonElement stageBattles)
    {
        var arrays = new List<EncounterStageArray>();
        foreach (JsonElement array in stageBattles.EnumerateArray())
        {
            var places = ParseStagePlaces(array.GetProperty("places"));
            arrays.Add(new EncounterStageArray(unchecked((uint)array.GetProperty("addr").GetInt32()), places));
        }

        return arrays;
    }

    private static List<EncounterFieldBattles> ParseStagePlaces(JsonElement placesElement)
    {
        var places = new List<EncounterFieldBattles>();
        foreach (JsonElement place in placesElement.EnumerateArray())
        {
            var areas = place.GetProperty("areas").EnumerateArray().Select(ParseBattleList).ToList();
            places.Add(new EncounterFieldBattles(
                place.GetProperty("serial").GetInt32(),
                place.GetProperty("id").GetInt32(),
                place.GetProperty("pad8").GetInt32(),
                areas));
        }

        return places;
    }

    private static EncounterBattleList ParseBattleList(JsonElement area)
    {
        var battles = area.GetProperty("battles").EnumerateArray().Select(b => new EncounterAreaBattle(
            b.GetProperty("encounter").GetInt32(), b.GetProperty("stage").GetInt32(), b.GetProperty("music").GetInt32(),
            b.GetProperty("musicName").GetString() ?? throw new JsonException("battle music name missing"))).ToArray();
        return new EncounterBattleList(area.GetProperty("count").GetInt32(), battles);
    }

    private static DigimonData ParseDigimon(JsonElement e) =>
        new(e.GetProperty("id").GetInt32(), Ints(e.GetProperty("battleStats")), Ints(e.GetProperty("resistances")),
            Ints(e.GetProperty("skills")), e.GetProperty("pairTechnique").GetInt32(), Ints(e.GetProperty("statusResistances")),
            Ints(e.GetProperty("skillLevels")), Ints(e.GetProperty("knownLevels")), e.GetProperty("experienceLevel").GetInt32(),
            e.GetProperty("pairPartner").GetInt32(), e.GetProperty("experienceRate").GetInt32(), e.GetProperty("hp").GetInt32(),
            e.GetProperty("mp").GetInt32(), e.GetProperty("hpGrowth").GetInt32(), e.GetProperty("mpGrowth").GetInt32(),
            Ints(e.GetProperty("statGrowth")), Ints(e.GetProperty("resistGrowth")), Ints(e.GetProperty("blastForms")),
            e.GetProperty("nameId").GetInt32(), e.GetProperty("family").GetInt32(), false);

    private static BattleTableEntry ParseTableEntry(JsonElement e) =>
        new(e.GetProperty("id").GetInt32(), e.GetProperty("item").GetInt32(), e.GetProperty("itemChance").GetInt32(),
            e.GetProperty("nameId").GetInt32(), Ints(e.GetProperty("techniques")), Ints(e.GetProperty("stats")),
            Ints(e.GetProperty("resistances")), e.GetProperty("family").GetInt32(),
            [.. e.GetProperty("actions").EnumerateArray().Select(ParseBattleAction)],
            ParseBattleAction(e.GetProperty("counter")));

    private static DigivolutionEntry ParseEvolution(JsonElement e) =>
        new(e.GetProperty("digimonIndex").GetInt32(), e.GetProperty("digimonId").GetInt32(),
            [.. e.GetProperty("needs").EnumerateArray().Select(n => new EvolutionNeed(
                n.GetProperty("digimonIndex").GetInt32(), n.GetProperty("digimonId").GetInt32(), n.GetProperty("level").GetInt32()))],
            e.GetProperty("stat").GetInt32(), e.GetProperty("value").GetInt32());

    internal static BattleAction ParseBattleAction(JsonElement e) =>
        new(e.GetProperty("target").GetInt32(), e.GetProperty("condition").GetInt32(), e.GetProperty("conditionArgument").GetInt32());

    internal static int[] Ints(JsonElement element) => [.. element.EnumerateArray().Select(v => v.GetInt32())];
}
