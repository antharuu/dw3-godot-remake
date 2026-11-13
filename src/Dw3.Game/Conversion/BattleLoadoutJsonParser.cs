using System.Globalization;
using System.Text.Json;

namespace Dw3Production.Conversion;

/// <summary>Static JSON parsing helpers for the battle loadout domains read from battle.json:
/// items, text tables, HUD frames, report sprites, menu icons and technique data.
/// Helpers that reference pack-local image paths receive a resolver delegate.</summary>
internal static class BattleLoadoutJsonParser
{
    /// <summary>Parses the battle items section of the battle manifest.</summary>
    /// <param name="root">The root <see cref="JsonElement"/> of the battle manifest.</param>
    /// <param name="names">The pre-loaded FSITMNAM token table for item names.</param>
    /// <returns>The fully parsed <see cref="BattleItemsData"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">The item count is wrong or an effect field is missing.</exception>
    internal static BattleItemsData ParseBattleItems(JsonElement root, List<IReadOnlyList<TextToken>> names)
    {
        var items = new List<BattleItemData>();
        foreach (JsonElement entry in root.GetProperty("items").EnumerateArray())
        {
            int id = entry.GetProperty("id").GetInt32();
            if (!TryItemSemantics(id, out string effect, out string target))
            {
                continue;
            }

            JsonElement effectData = entry.GetProperty("effect");
            if (effectData.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException($"battle item {id} has no effect data");
            }

            if (id >= names.Count)
            {
                throw new JsonException($"battle item {id} has no name");
            }

            int type = entry.GetProperty("type").GetInt32();
            items.Add(new BattleItemData(
                id,
                id,
                names[id],
                effectData.GetProperty("flags").GetInt32(),
                effectData.GetProperty("kind").GetInt32(),
                effectData.GetProperty("amount").GetInt32(),
                entry.GetProperty("price").GetInt32(),
                entry.GetProperty("sellPrice").GetInt32(),
                type,
                type + 70,
                effect,
                target));
        }

        if (items.Count != 29)
        {
            throw new JsonException($"battle item inventory has {items.Count} entries");
        }

        return new BattleItemsData(items);
    }

    /// <summary>Parses the battle text tables into a <see cref="BattleTextData"/>.</summary>
    /// <param name="menu">MFIGHT token table.</param>
    /// <param name="names">DIGNAM token table.</param>
    /// <param name="techniques">SKLNAM token table.</param>
    /// <param name="skillInfo">SKLINF token table.</param>
    /// <param name="itemInfo">ITMINF token table.</param>
    /// <param name="report">FGTRPT token table.</param>
    /// <returns>The fully parsed <see cref="BattleTextData"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">A table count is wrong.</exception>
    internal static BattleTextData ParseBattleText(
        List<IReadOnlyList<TextToken>> menu,
        List<IReadOnlyList<TextToken>> names,
        List<IReadOnlyList<TextToken>> techniques,
        List<IReadOnlyList<TextToken>> skillInfo,
        List<IReadOnlyList<TextToken>> itemInfo,
        List<IReadOnlyList<TextToken>> report)
    {
        if (menu.Count != 145 || names.Count != 251 || techniques.Count != 444 || skillInfo.Count != 220 || itemInfo.Count != 403 || report.Count != 12)
        {
            throw new JsonException("battle text table inventory mismatch");
        }

        var commands = Enumerable.Range(1, 6).Select(e => new BattleTextTemplate(e, "menu", [], "disc battle menu")).ToArray();
        var messages = new Dictionary<string, BattleTextTemplate>
        {
            ["actor"] = Template(22, "actor"),
            ["basicAttack"] = Template(8, "actor"),
            ["damage"] = Template(11, "value"),
            ["digivolve"] = Template(55, "actor", "digimon"),
            ["fledPlayer"] = Template(67, "actor"),
            ["item"] = Template(9, "actor", "item"),
            ["lost"] = Template(83),
            ["missed"] = Template(0x1D),
            ["multiHitDamage"] = Template(0x23, "value", "value", "value"),
            ["noEffect"] = Template(0x2F),
            ["noMp"] = Template(37),
            ["recovered"] = Template(0x22, "value"),
            ["sideRecovered"] = Template(0x22, "value"),
            ["switch"] = Template(56, "actor"),
            ["technique"] = Template(9, "actor", "technique"),
            ["won"] = Template(82),
        };
        var reportTemplates = new Dictionary<string, int>
        {
            ["digimonLearned"] = 8,
            ["expGained"] = 2,
            ["expLabel"] = 1,
            ["itemGained"] = 11,
            ["levelGained"] = 4,
            ["levelLabel"] = 3,
            ["moneyGained"] = 10,
            ["partnerLevel"] = 7,
            ["skillKnown"] = 6,
            ["skillLearned"] = 5,
            ["slotsFull"] = 9,
        };
        return new BattleTextData(menu, names, techniques, skillInfo, itemInfo, report, commands, messages, reportTemplates);
    }

    /// <summary>Parses the battle HUD manifest into a <see cref="BattleHudData"/>.</summary>
    /// <param name="root">The root <see cref="JsonElement"/> of the HUD manifest.</param>
    /// <param name="resolver">Delegate that resolves a pack-relative image path to an absolute path.</param>
    /// <returns>The fully parsed <see cref="BattleHudData"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">A required HUD field is missing or the frame count is wrong.</exception>
    internal static BattleHudData ParseBattleHud(JsonElement root, Func<string, string, string> resolver)
    {
        int[] ids = BattleEncounterJsonParser.Ints(root.GetProperty("frameIds"));
        var frames = new Dictionary<int, BattleHudFrame>();
        foreach (JsonProperty property in root.GetProperty("frames").EnumerateObject())
        {
            int id = int.Parse(property.Name, CultureInfo.InvariantCulture);
            JsonElement frame = property.Value;
            string[] rows = [.. frame.GetProperty("clutRows").EnumerateArray().Select(row =>
                resolver("battle/hud/" + (row.GetProperty("path").GetString() ?? throw new JsonException("HUD frame path missing")), $"HUD frame {id}"))];
            if (rows.Length != 5)
            {
                throw new JsonException($"HUD frame {id} lacks five palettes");
            }

            frames.Add(id, new BattleHudFrame(
                id,
                rows,
                frame.GetProperty("w").GetInt32(),
                frame.GetProperty("h").GetInt32(),
                frame.GetProperty("offsetX").GetInt32(),
                frame.GetProperty("offsetY").GetInt32()));
        }

        if (frames.Count != ids.Length || ids.Any(id => !frames.ContainsKey(id)))
        {
            throw new JsonException("HUD frame inventory mismatch");
        }

        JsonElement cursor = root.GetProperty("cursor");
        var hudCursor = new BattleHudCursor(
            resolver("battle/hud/" + cursor.GetProperty("path").GetString()!, "battle cursor"),
            resolver("battle/hud/" + cursor.GetProperty("offPath").GetString()!, "battle cursor off"),
            cursor.GetProperty("w").GetInt32(),
            cursor.GetProperty("h").GetInt32(),
            cursor.GetProperty("frames").GetInt32(),
            cursor.GetProperty("frameW").GetInt32(),
            cursor.GetProperty("frameH").GetInt32());
        return new BattleHudData(frames, hudCursor);
    }

    /// <summary>Parses the battle report sprites manifest into a <see cref="BattleReportSprites"/>.</summary>
    /// <param name="root">The root <see cref="JsonElement"/> of the report sprites manifest.</param>
    /// <param name="resolver">Delegate that resolves a pack-relative image path to an absolute path.</param>
    /// <returns>The fully parsed <see cref="BattleReportSprites"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">The animation or arrow row count is wrong.</exception>
    internal static BattleReportSprites ParseBattleReportSprites(JsonElement root, Func<string, string, string> resolver)
    {
        var frames = new Dictionary<int, BattleHudFrame>();
        foreach (JsonProperty property in root.GetProperty("frames").EnumerateObject())
        {
            int id = int.Parse(property.Name, CultureInfo.InvariantCulture);
            JsonElement frame = property.Value;
            string[] rows = [.. frame.GetProperty("clutRows").EnumerateArray().Select(row =>
                resolver("battle/report/" + row.GetProperty("path").GetString()!, $"report frame {id}"))];
            frames.Add(id, new BattleHudFrame(
                id,
                rows,
                frame.GetProperty("w").GetInt32(),
                frame.GetProperty("h").GetInt32(),
                frame.GetProperty("offsetX").GetInt32(),
                frame.GetProperty("offsetY").GetInt32()));
        }

        int[][] animations = [.. root.GetProperty("animations").EnumerateArray().Select(BattleEncounterJsonParser.Ints)];
        JsonElement arrow = root.GetProperty("arrow");
        string[] arrowRows = [.. arrow.GetProperty("clutRows").EnumerateArray().Select(row =>
            resolver("battle/report/" + row.GetProperty("path").GetString()!, "report arrow"))];
        if (animations.Length != 8 || animations.Any(row => row.Length != 7) || arrowRows.Length != 5)
        {
            throw new JsonException("battle report shape mismatch");
        }

        return new BattleReportSprites(
            frames,
            animations,
            new BattleHudFrame(
                10,
                arrowRows,
                arrow.GetProperty("w").GetInt32(),
                arrow.GetProperty("h").GetInt32(),
                arrow.GetProperty("offsetX").GetInt32(),
                arrow.GetProperty("offsetY").GetInt32()));
    }

    /// <summary>Parses the battle menu icons manifest into a <see cref="BattleMenuIconsData"/>.</summary>
    /// <param name="root">The root <see cref="JsonElement"/> of the menu icons manifest.</param>
    /// <param name="resolver">Delegate that resolves a pack-relative image path to an absolute path.</param>
    /// <returns>The fully parsed <see cref="BattleMenuIconsData"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">A required field is missing.</exception>
    internal static BattleMenuIconsData ParseBattleMenuIcons(JsonElement root, Func<string, string, string> resolver)
    {
        var frames = new Dictionary<int, BattleMenuIconFrame>();
        foreach (JsonProperty property in root.GetProperty("frames").EnumerateObject())
        {
            int id = int.Parse(property.Name, CultureInfo.InvariantCulture);
            JsonElement frame = property.Value;
            string[] rows = [.. frame.GetProperty("paths").EnumerateArray().Select(path =>
                resolver(NormalizePackPath("battle/menu-icons", path.GetString()), $"menu icon {id}"))];
            int[] offset = BattleEncounterJsonParser.Ints(frame.GetProperty("offset"));
            frames.Add(id, new BattleMenuIconFrame(id, rows, frame.GetProperty("w").GetInt32(), frame.GetProperty("h").GetInt32(), offset[0], offset[1]));
        }

        JsonElement arrow = root.GetProperty("messageArrow");
        int[] position = BattleEncounterJsonParser.Ints(arrow.GetProperty("position"));
        return new BattleMenuIconsData(frames, arrow.GetProperty("frame").GetInt32(), position[0], position[1], arrow.GetProperty("timeStep").GetInt32());
    }

    /// <summary>Parses the techniques section of the battle manifest.</summary>
    /// <param name="root">The root <see cref="JsonElement"/> of the battle manifest.</param>
    /// <returns>The fully parsed <see cref="TechniqueData"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">The technique count is wrong.</exception>
    internal static TechniqueData ParseTechs(JsonElement root)
    {
        var techs = new Dictionary<int, TechData>();
        foreach (JsonElement entry in root.GetProperty("techniques").EnumerateArray())
        {
            var value = new TechData(
                entry.GetProperty("id").GetInt32(),
                entry.GetProperty("mp").GetInt32(),
                entry.GetProperty("power").GetInt32(),
                entry.GetProperty("icon").GetInt32(),
                entry.GetProperty("kind").GetInt32(),
                entry.GetProperty("accuracy").GetInt32(),
                entry.GetProperty("element").GetInt32(),
                entry.GetProperty("elementPower").GetInt32(),
                entry.GetProperty("family").GetInt32(),
                entry.GetProperty("effect").GetInt32(),
                entry.GetProperty("effectChance").GetInt32(),
                entry.GetProperty("effectPower").GetInt32(),
                entry.GetProperty("scriptStage").GetInt32(),
                entry.GetProperty("scriptEffect").GetInt32(),
                entry.GetProperty("scriptSound").GetInt32(),
                entry.GetProperty("script").GetInt32(),
                entry.GetProperty("hitCount").GetInt32());
            techs.Add(value.Id, value);
        }

        if (techs.Count != 443)
        {
            throw new JsonException($"technique inventory has {techs.Count} entries");
        }

        return new TechniqueData(techs);
    }

    /// <summary>Parses a text table JSON file (MFIGHT, DIGNAM, etc.) into token lists.</summary>
    /// <param name="path">Absolute path to the JSON text file.</param>
    /// <param name="context">Human-readable context name for error messages.</param>
    /// <returns>A list of token lists, one per text entry.</returns>
    /// <exception cref="CacheResourceException">The file is absent or its JSON is malformed.</exception>
    internal static List<IReadOnlyList<TextToken>> ParseTextTable(string path, string context)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            var entries = new List<IReadOnlyList<TextToken>>();
            foreach (JsonElement entry in document.RootElement.GetProperty("entries").EnumerateArray())
            {
                entries.Add([.. entry.EnumerateArray().Select(ParseTextToken)]);
            }

            return entries;
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            throw new CacheResourceException("CACHE_INVALID", $"{context} is unreadable: {error.Message}");
        }
    }

    /// <summary>Decodes one serialized text token: kind 0 is a glyph (<c>g</c>), kind 1 an inline icon (<c>i</c>), kind 2 a control code (<c>c</c>) with its argument list.</summary>
    /// <param name="token">A token object with <c>kind</c>, <c>Code</c> and, for control codes, <c>Args</c>.</param>
    /// <returns>The runtime token.</returns>
    /// <exception cref="JsonException">The kind is not 0, 1 or 2.</exception>
    private static TextToken ParseTextToken(JsonElement token) => token.GetProperty("kind").GetInt32() switch
    {
        0 => new TextToken("g", token.GetProperty("Code").GetInt32(), 0, []),
        1 => new TextToken("i", token.GetProperty("Code").GetInt32(), 0, []),
        2 => new TextToken("c", 0, token.GetProperty("Code").GetInt32(), BattleEncounterJsonParser.Ints(token.GetProperty("Args"))),
        int kind => throw new JsonException($"unknown text token kind {kind}"),
    };

    /// <summary>Builds a battle message template pointing at an entry of the battle menu text table.</summary>
    /// <param name="entry">Entry index in the battle menu text table.</param>
    /// <param name="substitutions">Names of the values substituted into the message, in display order.</param>
    /// <returns>A template tagged as coming from the disc battle behavior.</returns>
    private static BattleTextTemplate Template(int entry, params string[] substitutions) =>
        new(entry, "message", substitutions, "disc battle behavior");

    /// <summary>Classifies a battle item by its id range: healing items 0x2B..0x2E, status cures 0x42..0x45, revive 0x46, then the single-id boosts and enemy-targeted items up to 0x5A.</summary>
    /// <param name="id">Item id from the item table.</param>
    /// <param name="effect">On success, the effect name such as <c>heal</c> or <c>boostElement</c>, otherwise empty.</param>
    /// <param name="target">On success, <c>partner</c>, <c>party</c>, <c>field</c> or <c>enemy</c>, otherwise empty.</param>
    /// <returns><see langword="true"/> when the id falls in a known range, <see langword="false"/> for items with no battle use.</returns>
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
        foreach (var range in ranges)
        {
            if (id >= range.Low && id <= range.High)
            {
                effect = range.Effect;
                target = range.Target;
                return true;
            }
        }

        effect = target = string.Empty;
        return false;
    }

    /// <summary>Makes a resource path written in the battle manifest pack-relative: paths already under <c>battle/</c> are kept, bare names are prefixed with the given directory.</summary>
    /// <param name="directory">Pack directory used as prefix for bare names.</param>
    /// <param name="path">Path as written in the manifest.</param>
    /// <returns>A pack-relative path.</returns>
    /// <exception cref="JsonException">The path is null, empty or whitespace.</exception>
    private static string NormalizePackPath(string directory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new JsonException($"{directory} resource path missing");
        }

        return path.StartsWith("battle/", StringComparison.Ordinal) ? path : $"{directory}/{path}";
    }
}
