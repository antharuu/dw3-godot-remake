using System.Text.Json;
using Dw3.Data;
using Microsoft.Data.Sqlite;

namespace Dw3Production.Conversion;

/// <summary>Reads combat manifests and assets exclusively from the validated extracted data pack.</summary>
public sealed class BattleDataPackLoader : IDisposable
{
    private readonly DataPackReader _pack;
    private readonly SqliteConnection _database;

    public BattleDataPackLoader(DataPackReader pack)
    {
        _pack = pack;
        _database = pack.OpenDatabase();
    }

    public string ModelPath(int fighterId)
    {
        using SqliteCommand command = _database.CreateCommand();
        command.CommandText = "SELECT status, reason, manifest_path FROM battle_models WHERE fighter_id=$id";
        command.Parameters.AddWithValue("$id", fighterId);
        using SqliteDataReader row = command.ExecuteReader();
        if (!row.Read()) throw Missing($"battle model {fighterId} is absent from the data pack");
        string status = row.GetString(0);
        if (!string.Equals(status, "decoded", StringComparison.Ordinal) || row.IsDBNull(2))
            throw Invalid($"battle model {fighterId} was not decoded: {(row.IsDBNull(1) ? "no reason recorded" : row.GetString(1))}");
        string path = RequireFile(row.GetString(2), $"battle model {fighterId}");
        if (row.Read()) throw Invalid($"battle model {fighterId} has duplicate records");
        return path;
    }

    public string StagePath(int stageId) => RequireFile($"battle/stages/stage-{stageId}-model.json", $"battle stage {stageId}");

    public EncounterData LoadEncounters()
    {
        try
        {
            using JsonDocument document = ReadJson("encounter/encounter.json", "encounter manifest");
            JsonElement root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "m6-encounter-v1" || root.GetProperty("edition").GetString() != "SLES-03936")
                throw new JsonException("encounter manifest schema mismatch");
            int[] battleRates = Ints(root.GetProperty("battleRates"));
            var enemies = root.GetProperty("enemies").EnumerateArray().Select(enemy => new BattleEnemy(
                enemy.GetProperty("index").GetInt32(), enemy.GetProperty("fighter").GetInt32(), enemy.GetProperty("level").GetInt32(),
                enemy.GetProperty("hp").GetInt32(), enemy.GetProperty("mp").GetInt32(), enemy.GetProperty("strength").GetInt32())).ToArray();
            var encounters = root.GetProperty("encounters").EnumerateArray().Select(encounter => new EncounterEntry(
                encounter.GetProperty("index").GetInt32(), Ints(encounter.GetProperty("enemyIndices")),
                [.. encounter.GetProperty("enemies").EnumerateArray().Select(enemy => enemy.GetProperty("fighter").GetInt32())],
                encounter.GetProperty("ambushChance").GetInt32(), encounter.GetProperty("kind").GetInt32(), Ints(encounter.GetProperty("blocks")))).ToArray();
            var stages = new Dictionary<string, EncounterStage>();
            foreach (JsonProperty stageProperty in root.GetProperty("stages").EnumerateObject())
            {
                JsonElement stage = stageProperty.Value;
                var arrays = new List<EncounterStageArray>();
                foreach (JsonElement array in stage.GetProperty("stageBattles").EnumerateArray())
                {
                    var places = new List<EncounterFieldBattles>();
                    foreach (JsonElement place in array.GetProperty("places").EnumerateArray())
                    {
                        var areas = new List<EncounterBattleList>();
                        foreach (JsonElement area in place.GetProperty("areas").EnumerateArray())
                        {
                            var battles = area.GetProperty("battles").EnumerateArray().Select(battle => new EncounterAreaBattle(
                                battle.GetProperty("encounter").GetInt32(), battle.GetProperty("stage").GetInt32(), battle.GetProperty("music").GetInt32(),
                                battle.GetProperty("musicName").GetString() ?? throw new JsonException("battle music name missing"))).ToArray();
                            areas.Add(new EncounterBattleList(area.GetProperty("count").GetInt32(), battles));
                        }
                        places.Add(new EncounterFieldBattles(place.GetProperty("serial").GetInt32(), place.GetProperty("id").GetInt32(), place.GetProperty("pad8").GetInt32(), areas));
                    }
                    arrays.Add(new EncounterStageArray(unchecked((uint)array.GetProperty("addr").GetInt32()), places));
                }
                stages.Add(stageProperty.Name, new EncounterStage(stage.GetProperty("overlay").GetString() ?? throw new JsonException("stage overlay missing"),
                    stage.GetProperty("overlay_sha256").GetString() ?? throw new JsonException("stage hash missing"), arrays));
            }
            if (enemies.Length == 0 || encounters.Length == 0 || stages.Count == 0) throw new JsonException("encounter manifest is empty");
            return new EncounterData(battleRates, enemies, encounters, stages);
        }
        catch (CacheResourceException) { throw; }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        { throw Invalid($"encounter manifest invalid: {error.Message}"); }
    }

    public FighterData LoadFighters()
    {
        try
        {
            using JsonDocument document = ReadBattleJson();
            JsonElement root = document.RootElement;
            var ordered = new List<DigimonData>();
            var digimon = new Dictionary<int, DigimonData>();
            foreach (JsonElement entry in root.GetProperty("digimon").EnumerateArray())
            {
                var value = new DigimonData(entry.GetProperty("id").GetInt32(), Ints(entry.GetProperty("battleStats")), Ints(entry.GetProperty("resistances")),
                    Ints(entry.GetProperty("skills")), entry.GetProperty("pairTechnique").GetInt32(), Ints(entry.GetProperty("statusResistances")),
                    Ints(entry.GetProperty("skillLevels")), Ints(entry.GetProperty("knownLevels")), entry.GetProperty("experienceLevel").GetInt32(),
                    entry.GetProperty("pairPartner").GetInt32(), entry.GetProperty("experienceRate").GetInt32(), entry.GetProperty("hp").GetInt32(),
                    entry.GetProperty("mp").GetInt32(), entry.GetProperty("hpGrowth").GetInt32(), entry.GetProperty("mpGrowth").GetInt32(),
                    Ints(entry.GetProperty("statGrowth")), Ints(entry.GetProperty("resistGrowth")), Ints(entry.GetProperty("blastForms")),
                    entry.GetProperty("nameId").GetInt32(), entry.GetProperty("family").GetInt32(), false);
                digimon.Add(value.Id, value); ordered.Add(value);
            }
            var table = new List<BattleTableEntry>();
            foreach (JsonElement entry in root.GetProperty("enemyTable").EnumerateArray())
                table.Add(new BattleTableEntry(entry.GetProperty("id").GetInt32(), entry.GetProperty("item").GetInt32(), entry.GetProperty("itemChance").GetInt32(),
                    entry.GetProperty("nameId").GetInt32(), Ints(entry.GetProperty("techniques")), Ints(entry.GetProperty("stats")),
                    Ints(entry.GetProperty("resistances")), entry.GetProperty("family").GetInt32(),
                    [.. entry.GetProperty("actions").EnumerateArray().Select(Action)], Action(entry.GetProperty("counter"))));
            if (digimon.Count != 55 || table.Count == 0) throw new JsonException("fighter inventory is incomplete");
            return new FighterData(digimon, ordered, table);
        }
        catch (CacheResourceException) { throw; }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        { throw Invalid($"fighter data invalid: {error.Message}"); }
    }

    public DigivolutionData LoadDigivolutions()
    {
        try
        {
            using JsonDocument document = ReadJson("digivolution/digivolution.json", "digivolution data");
            JsonElement root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "m9b-digivolution-v2") throw new JsonException("digivolution schema mismatch");
            var partners = new List<PartnerDigivolution>();
            foreach (JsonElement partner in root.GetProperty("partners").EnumerateArray())
            {
                var evolutions = partner.GetProperty("evolutions").EnumerateArray().Select(entry => new DigivolutionEntry(
                    entry.GetProperty("digimonIndex").GetInt32(), entry.GetProperty("digimonId").GetInt32(),
                    [.. entry.GetProperty("needs").EnumerateArray().Select(need => new EvolutionNeed(need.GetProperty("digimonIndex").GetInt32(), need.GetProperty("digimonId").GetInt32(), need.GetProperty("level").GetInt32()))],
                    entry.GetProperty("stat").GetInt32(), entry.GetProperty("value").GetInt32())).ToArray();
                if (evolutions.Length != 44) throw new JsonException("digivolution list must contain 44 entries");
                partners.Add(new PartnerDigivolution(partner.GetProperty("partner").GetInt32(), partner.GetProperty("baseId").GetInt32(), evolutions));
            }
            if (partners.Count != 8) throw new JsonException("digivolution data must contain eight partners");
            return new DigivolutionData(partners, new DigivolutionBlast([4, 19, 39, 70]));
        }
        catch (CacheResourceException) { throw; }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        { throw Invalid($"digivolution data invalid: {error.Message}"); }
    }

    public BattleItemsData LoadBattleItems()
    {
        try
        {
            using JsonDocument document = ReadBattleJson();
            IReadOnlyList<IReadOnlyList<TextToken>> names = LoadTextTable("battle/text/fsitmnam.json", "item names");
            var items = new List<BattleItemData>();
            foreach (JsonElement entry in document.RootElement.GetProperty("items").EnumerateArray())
            {
                int id = entry.GetProperty("id").GetInt32();
                if (!TryItemSemantics(id, out string effect, out string target)) continue;
                JsonElement effectData = entry.GetProperty("effect");
                if (effectData.ValueKind != JsonValueKind.Object) throw new JsonException($"battle item {id} has no effect data");
                if (id >= names.Count) throw new JsonException($"battle item {id} has no name");
                int type = entry.GetProperty("type").GetInt32();
                items.Add(new BattleItemData(id, id, names[id], effectData.GetProperty("flags").GetInt32(), effectData.GetProperty("kind").GetInt32(),
                    effectData.GetProperty("amount").GetInt32(), entry.GetProperty("price").GetInt32(), entry.GetProperty("sellPrice").GetInt32(), type, type + 70, effect, target));
            }
            if (items.Count != 29) throw new JsonException($"battle item inventory has {items.Count} entries");
            return new BattleItemsData(items);
        }
        catch (CacheResourceException) { throw; }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        { throw Invalid($"battle item data invalid: {error.Message}"); }
    }

    public RewardsData LoadRewards()
    {
        try
        {
            using JsonDocument document = ReadBattleJson();
            JsonElement root = document.RootElement;
            var rewards = root.GetProperty("rewards").EnumerateArray().Select(entry => new RewardEntry(entry.GetProperty("id").GetInt32(),
                entry.GetProperty("digimonExperience").GetInt32(), entry.GetProperty("experience").GetInt32(), entry.GetProperty("money").GetInt32())).ToArray();
            JsonElement growth = root.GetProperty("growth");
            var tables = new GrowthTablesData(Ints(growth.GetProperty("levelExperience")), Ints(growth.GetProperty("vitalCuts")), Ints(growth.GetProperty("vitalRandom")),
                [.. growth.GetProperty("statGains").EnumerateArray().Select(Ints).Cast<IReadOnlyList<int>>()], Ints(growth.GetProperty("resistanceGains")));
            if (rewards.Length != 335) throw new JsonException($"reward inventory has {rewards.Length} entries");
            return new RewardsData(rewards, tables);
        }
        catch (CacheResourceException) { throw; }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        { throw Invalid($"battle reward data invalid: {error.Message}"); }
    }

    public BattleTextData LoadBattleText()
    {
        try
        {
            IReadOnlyList<IReadOnlyList<TextToken>> menu = LoadTextTable("battle/text/fsmfight.json", "battle menu text");
            IReadOnlyList<IReadOnlyList<TextToken>> names = LoadTextTable("battle/text/fsdignam.json", "Digimon names");
            IReadOnlyList<IReadOnlyList<TextToken>> techniques = LoadTextTable("battle/text/fssklnam.json", "technique names");
            IReadOnlyList<IReadOnlyList<TextToken>> skillInfo = LoadTextTable("battle/text/fssklinf.json", "technique descriptions");
            IReadOnlyList<IReadOnlyList<TextToken>> itemInfo = LoadTextTable("battle/text/fsitminf.json", "item descriptions");
            IReadOnlyList<IReadOnlyList<TextToken>> report = LoadTextTable("battle/text/fsfgtrpt.json", "battle report text");
            if (menu.Count != 145 || names.Count != 251 || techniques.Count != 444 || skillInfo.Count != 220 || itemInfo.Count != 403 || report.Count != 12)
                throw new JsonException("battle text table inventory mismatch");
            var commands = Enumerable.Range(1, 6).Select(entry => new BattleTextTemplate(entry, "menu", [], "disc battle menu")).ToArray();
            var messages = new Dictionary<string, BattleTextTemplate>
            {
                ["actor"] = Template(22, "actor"),
                ["basicAttack"] = Template(8, "actor"),
                ["damage"] = Template(11, "value"),
                ["digivolve"] = Template(55, "actor", "digimon"),
                ["fledPlayer"] = Template(67, "actor"),
                ["item"] = Template(9, "actor", "item"),
                ["lost"] = Template(83),
                ["noMp"] = Template(37),
                ["switch"] = Template(56, "actor"),
                ["technique"] = Template(9, "actor", "technique"),
                ["won"] = Template(82),
            };
            var reportTemplates = new Dictionary<string, int> { ["digimonLearned"] = 8, ["expGained"] = 2, ["expLabel"] = 1, ["itemGained"] = 11, ["levelGained"] = 4, ["moneyGained"] = 10, ["partnerLevel"] = 7, ["skillKnown"] = 6, ["skillLearned"] = 5 };
            return new BattleTextData(menu, names, techniques, skillInfo, itemInfo, report, commands, messages, reportTemplates);
        }
        catch (CacheResourceException) { throw; }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        { throw Invalid($"battle text data invalid: {error.Message}"); }
    }

    public BattleHudData LoadBattleHud()
    {
        try
        {
            using JsonDocument document = ReadJson("battle/hud/hud.json", "battle HUD");
            JsonElement root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "m6-battle-hud-v3") throw new JsonException("battle HUD schema mismatch");
            int[] ids = Ints(root.GetProperty("frameIds"));
            var frames = new Dictionary<int, BattleHudFrame>();
            foreach (JsonProperty property in root.GetProperty("frames").EnumerateObject())
            {
                int id = int.Parse(property.Name, System.Globalization.CultureInfo.InvariantCulture);
                JsonElement frame = property.Value;
                string[] rows = [.. frame.GetProperty("clutRows").EnumerateArray().Select(row => RequireFile("battle/hud/" + (row.GetProperty("path").GetString() ?? throw new JsonException("HUD frame path missing")), $"HUD frame {id}"))];
                if (rows.Length != 5) throw new JsonException($"HUD frame {id} lacks five palettes");
                frames.Add(id, new BattleHudFrame(id, rows, frame.GetProperty("w").GetInt32(), frame.GetProperty("h").GetInt32(), frame.GetProperty("offsetX").GetInt32(), frame.GetProperty("offsetY").GetInt32()));
            }
            if (frames.Count != ids.Length || ids.Any(id => !frames.ContainsKey(id))) throw new JsonException("HUD frame inventory mismatch");
            JsonElement cursor = root.GetProperty("cursor");
            return new BattleHudData(frames, new BattleHudCursor(RequireFile("battle/hud/" + cursor.GetProperty("path").GetString()!, "battle cursor"),
                RequireFile("battle/hud/" + cursor.GetProperty("offPath").GetString()!, "battle cursor off"), cursor.GetProperty("w").GetInt32(), cursor.GetProperty("h").GetInt32(),
                cursor.GetProperty("frames").GetInt32(), cursor.GetProperty("frameW").GetInt32(), cursor.GetProperty("frameH").GetInt32()));
        }
        catch (CacheResourceException) { throw; }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        { throw Invalid($"battle HUD invalid: {error.Message}"); }
    }

    public BattleReportSprites LoadBattleReportSprites()
    {
        try
        {
            using JsonDocument document = ReadJson("battle/report/report.json", "battle report sprites");
            JsonElement root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "m6-battle-report-sprites-v1") throw new JsonException("battle report schema mismatch");
            var frames = new Dictionary<int, BattleHudFrame>();
            foreach (JsonProperty property in root.GetProperty("frames").EnumerateObject())
            {
                int id = int.Parse(property.Name, System.Globalization.CultureInfo.InvariantCulture); JsonElement frame = property.Value;
                string[] rows = [.. frame.GetProperty("clutRows").EnumerateArray().Select(row => RequireFile("battle/report/" + row.GetProperty("path").GetString()!, $"report frame {id}"))];
                frames.Add(id, new BattleHudFrame(id, rows, frame.GetProperty("w").GetInt32(), frame.GetProperty("h").GetInt32(), frame.GetProperty("offsetX").GetInt32(), frame.GetProperty("offsetY").GetInt32()));
            }
            int[][] animations = [.. root.GetProperty("animations").EnumerateArray().Select(Ints)];
            JsonElement arrow = root.GetProperty("arrow");
            string[] arrowRows = [.. arrow.GetProperty("clutRows").EnumerateArray().Select(row => RequireFile("battle/report/" + row.GetProperty("path").GetString()!, "report arrow"))];
            if (animations.Length != 8 || animations.Any(row => row.Length != 7) || arrowRows.Length != 5) throw new JsonException("battle report shape mismatch");
            return new BattleReportSprites(frames, animations, new BattleHudFrame(10, arrowRows, arrow.GetProperty("w").GetInt32(), arrow.GetProperty("h").GetInt32(), arrow.GetProperty("offsetX").GetInt32(), arrow.GetProperty("offsetY").GetInt32()));
        }
        catch (CacheResourceException) { throw; }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        { throw Invalid($"battle report sprites invalid: {error.Message}"); }
    }

    public BattleMenuIconsData LoadBattleMenuIcons()
    {
        try
        {
            using JsonDocument document = ReadJson("battle/menu-icons/manifest.json", "battle menu icons"); JsonElement root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "m6-battle-menu-icons-v1") throw new JsonException("battle menu icon schema mismatch");
            var frames = new Dictionary<int, BattleMenuIconFrame>();
            foreach (JsonProperty property in root.GetProperty("frames").EnumerateObject())
            {
                int id = int.Parse(property.Name, System.Globalization.CultureInfo.InvariantCulture); JsonElement frame = property.Value;
                string[] rows = [.. frame.GetProperty("paths").EnumerateArray().Select(path => RequireFile(NormalizePackPath("battle/menu-icons", path.GetString()), $"menu icon {id}"))];
                int[] offset = Ints(frame.GetProperty("offset"));
                frames.Add(id, new BattleMenuIconFrame(id, rows, frame.GetProperty("w").GetInt32(), frame.GetProperty("h").GetInt32(), offset[0], offset[1]));
            }
            JsonElement arrow = root.GetProperty("messageArrow"); int[] position = Ints(arrow.GetProperty("position"));
            return new BattleMenuIconsData(frames, arrow.GetProperty("frame").GetInt32(), position[0], position[1], arrow.GetProperty("timeStep").GetInt32());
        }
        catch (CacheResourceException) { throw; }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        { throw Invalid($"battle menu icons invalid: {error.Message}"); }
    }

    public TechniqueData LoadTechs()
    {
        try
        {
            using JsonDocument document = ReadBattleJson();
            var techs = new Dictionary<int, TechData>();
            foreach (JsonElement entry in document.RootElement.GetProperty("techniques").EnumerateArray())
            {
                var value = new TechData(entry.GetProperty("id").GetInt32(), entry.GetProperty("mp").GetInt32(), entry.GetProperty("power").GetInt32(),
                    entry.GetProperty("icon").GetInt32(), entry.GetProperty("kind").GetInt32(), entry.GetProperty("accuracy").GetInt32(),
                    entry.GetProperty("element").GetInt32(), entry.GetProperty("elementPower").GetInt32(), entry.GetProperty("family").GetInt32(),
                    entry.GetProperty("effect").GetInt32(), entry.GetProperty("effectChance").GetInt32(), entry.GetProperty("effectPower").GetInt32(),
                    entry.GetProperty("scriptStage").GetInt32(), entry.GetProperty("scriptEffect").GetInt32(), entry.GetProperty("scriptSound").GetInt32(),
                    entry.GetProperty("script").GetInt32(), entry.GetProperty("hitCount").GetInt32());
                techs.Add(value.Id, value);
            }
            if (techs.Count != 443) throw new JsonException($"technique inventory has {techs.Count} entries");
            return new TechniqueData(techs);
        }
        catch (CacheResourceException) { throw; }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        { throw Invalid($"technique data invalid: {error.Message}"); }
    }

    private IReadOnlyList<IReadOnlyList<TextToken>> LoadTextTable(string relativePath, string subject)
    {
        using JsonDocument document = ReadJson(relativePath, subject);
        var entries = new List<IReadOnlyList<TextToken>>();
        foreach (JsonElement entry in document.RootElement.GetProperty("entries").EnumerateArray())
            entries.Add([.. entry.EnumerateArray().Select(token => token.GetProperty("kind").GetInt32() switch
            {
                0 => new TextToken("g", token.GetProperty("Code").GetInt32(), 0, []),
                1 => new TextToken("i", token.GetProperty("Code").GetInt32(), 0, []),
                2 => new TextToken("c", 0, token.GetProperty("Code").GetInt32(), Ints(token.GetProperty("Args"))),
                int kind => throw new JsonException($"unknown text token kind {kind}"),
            })]);
        return entries;
    }

    private static BattleTextTemplate Template(int entry, params string[] substitutions) => new(entry, "message", substitutions, "disc battle behavior");

    private static bool TryItemSemantics(int id, out string effect, out string target)
    {
        (int Low, int High, string Effect, string Target)[] ranges =
        [
            (0x2B, 0x2E, "heal", "partner"), (0x42, 0x45, "cureStatus", "partner"), (0x46, 0x46, "revive", "party"),
            (0x47, 0x47, "restoreHalfHpMp", "partner"), (0x48, 0x48, "boostSpeed", "partner"), (0x49, 0x49, "boostAttack", "partner"),
            (0x4A, 0x4A, "boostDefense", "partner"), (0x4B, 0x4B, "special", "partner"), (0x4C, 0x4C, "fillGauge", "partner"),
            (0x4D, 0x53, "boostElement", "field"), (0x54, 0x54, "boostRandomElement", "field"), (0x55, 0x55, "drain", "enemy"),
            (0x56, 0x56, "confuse", "enemy"), (0x57, 0x57, "weakenDefense", "enemy"), (0x58, 0x58, "lowerStat", "enemy"),
            (0x59, 0x59, "lowerSpeed", "enemy"), (0x5A, 0x5A, "itemTech", "enemy"),
        ];
        foreach (var range in ranges) if (id >= range.Low && id <= range.High) { effect = range.Effect; target = range.Target; return true; }
        effect = target = string.Empty; return false;
    }

    private JsonDocument ReadBattleJson()
    {
        JsonDocument document = ReadJson("battle/battle.json", "battle data");
        if (document.RootElement.GetProperty("schema").GetString() != "m9b-battle-v2")
        {
            document.Dispose();
            throw Invalid("battle data schema mismatch");
        }
        return document;
    }

    private JsonDocument ReadJson(string relativePath, string subject)
    {
        string path = RequireFile(relativePath, subject);
        try { return JsonDocument.Parse(File.ReadAllText(path)); }
        catch (Exception error) when (error is IOException or JsonException)
        { throw Invalid($"{subject} is unreadable: {error.Message}"); }
    }

    private static int[] Ints(JsonElement element) => [.. element.EnumerateArray().Select(value => value.GetInt32())];

    private static string NormalizePackPath(string directory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new JsonException($"{directory} resource path missing");
        return path.StartsWith("battle/", StringComparison.Ordinal) ? path : $"{directory}/{path}";
    }

    private static BattleAction Action(JsonElement element) => new(element.GetProperty("target").GetInt32(), element.GetProperty("condition").GetInt32(), element.GetProperty("conditionArgument").GetInt32());

    private string RequireFile(string relativePath, string subject)
    {
        string path;
        try { path = _pack.ResolvePath(relativePath); }
        catch (ArgumentException error) { throw new CacheResourceException("CACHE_INVALID", $"{subject} path is invalid: {error.Message}"); }
        if (!File.Exists(path)) throw Missing($"{subject} is missing: {path}");
        return path;
    }

    private static CacheResourceException Missing(string message) => new("CACHE_RESOURCE_UNAVAILABLE", message);
    private static CacheResourceException Invalid(string message) => new("CACHE_INVALID", message);

    public void Dispose() => _database.Dispose();
}
